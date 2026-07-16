// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System.Reflection.Metadata.ReadyToRun.Webcil
{
    /// <summary>
    /// Wrapper around a Webcil image that implements <see cref="IPlatformBinaryReader"/>.
    /// Webcil is a stripped-down PE format used for managed assemblies in WebAssembly environments.
    /// The image may be a raw <c>.webcil</c> file or a Webcil payload embedded inside a WASM module.
    /// </summary>
    /// <remarks>
    /// This is a port of the runtime's <c>WebcilImageReader</c>
    /// (<c>src/coreclr/tools/aot/ILCompiler.Reflection.ReadyToRun/WebcilImageReader.cs</c>),
    /// adapted to the structural reader's <see cref="IPlatformBinaryReader"/> abstraction. The
    /// r2rdump-only WASM disassembly helpers (elem/type/function section parsing) are intentionally
    /// omitted because the structural reader never disassembles code.
    /// </remarks>
    public sealed class WebcilImageReader : IPlatformBinaryReader
    {
        private readonly byte[] _image;
        private readonly GCHandle _pinnedArray;
        private readonly WebcilHeader _header;
        private readonly WebcilSectionHeader[] _sections;
        private readonly long _webcilOffset;
        private readonly DirectoryEntry _corHeaderMetadataDirectory;
        private readonly CorFlags _corFlags;
        private readonly DirectoryEntry _managedNativeHeaderDirectory;

        // Webcil doesn't encode machine type; wasm targets use a placeholder.
        public Machine Machine => WasmMachine.Wasm32;

        /// <summary>True when this Webcil image is wrapped inside a WASM module.</summary>
        public bool IsWasmWrapped => _webcilOffset > 0;

        public WebcilImageReader(byte[] image)
        {
            _image = image ?? throw new ArgumentNullException(nameof(image));
            _pinnedArray = GCHandle.Alloc(_image, GCHandleType.Pinned);

            try
            {
                _webcilOffset = 0;

                if (IsWasmModule(_image))
                {
                    if (!TryFindWebcilInWasm(_image, out _webcilOffset))
                        throw new BadImageFormatException("WASM module does not contain a Webcil payload");
                }

                if (!TryReadHeader(_image, _webcilOffset, out _header))
                    throw new BadImageFormatException("Not a valid Webcil file");

                _sections = ReadSections(_image, _webcilOffset, _header);

                ReadCorHeader(out _corFlags, out _corHeaderMetadataDirectory, out _managedNativeHeaderDirectory);
            }
            catch
            {
                if (_pinnedArray.IsAllocated)
                    _pinnedArray.Free();
                throw;
            }
        }

        ~WebcilImageReader()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool _)
        {
            if (_pinnedArray.IsAllocated)
                _pinnedArray.Free();
        }

        /// <summary>
        /// Detects whether a byte array is a Webcil image (raw or embedded inside a WASM module).
        /// </summary>
        public static bool IsWebcilImage(byte[] image)
        {
            if (image is null || image.Length < 4)
                return false;

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(image);
            if (magic == WebcilConstants.WEBCIL_MAGIC)
                return true;

            if (IsWasmModule(image))
                return TryFindWebcilInWasm(image, out _);

            return false;
        }

        /// <summary>
        /// Detects whether the file at the specified path is a Webcil image.
        /// </summary>
        public static bool IsWebcilImage(string filename)
        {
            try
            {
                using FileStream stream = File.OpenRead(filename);
                Span<byte> header = stackalloc byte[4];
                if (stream.Read(header) != 4)
                    return false;

                uint magic = BinaryPrimitives.ReadUInt32LittleEndian(header);
                if (magic == WebcilConstants.WEBCIL_MAGIC)
                    return true;

                // Check for WASM magic ('\0asm') and, if present, scan the whole file for a payload.
                if (header[0] == 0x00 && header[1] == 0x61 && header[2] == 0x73 && header[3] == 0x6D)
                {
                    stream.Seek(0, SeekOrigin.Begin);
                    byte[] fullImage = new byte[stream.Length];
                    stream.ReadExactly(fullImage);
                    return TryFindWebcilInWasm(fullImage, out _);
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public int GetOffset(int rva)
        {
            foreach (WebcilSectionHeader section in _sections)
            {
                if ((uint)rva >= section.VirtualAddress && (uint)rva < (ulong)section.VirtualAddress + section.VirtualSize)
                {
                    uint sectionRelative = (uint)rva - section.VirtualAddress;
                    if (sectionRelative >= section.SizeOfRawData)
                        throw new BadImageFormatException($"RVA 0x{rva:X} maps beyond section raw data");

                    long fileOffset = section.PointerToRawData + (long)sectionRelative + _webcilOffset;
                    if (fileOffset < 0 || fileOffset > _image.Length)
                        throw new BadImageFormatException($"RVA 0x{rva:X} maps outside the image");

                    return checked((int)fileOffset);
                }
            }

            throw new BadImageFormatException($"RVA 0x{rva:X} not found in any Webcil section");
        }

        /// <summary>
        /// Returns the number of raw bytes that remain in the section containing <paramref name="rva"/>.
        /// </summary>
        public int GetSectionRemainingSize(int rva)
        {
            foreach (WebcilSectionHeader section in _sections)
            {
                if ((uint)rva >= section.VirtualAddress && (uint)rva < (ulong)section.VirtualAddress + section.VirtualSize)
                {
                    uint sectionRelative = (uint)rva - section.VirtualAddress;
                    if (sectionRelative >= section.SizeOfRawData)
                        throw new BadImageFormatException($"RVA 0x{rva:X} maps beyond section raw data");

                    return (int)(section.SizeOfRawData - sectionRelative);
                }
            }

            throw new BadImageFormatException($"RVA 0x{rva:X} not found in any Webcil section");
        }

        public bool TryGetReadyToRunHeader(out int rva, out bool isComposite)
        {
            // Webcil R2R images use the COR header's ManagedNativeHeaderDirectory, same as a regular
            // (non-composite) PE R2R image.
            if ((_corFlags & CorFlags.ILLibrary) != 0 && _managedNativeHeaderDirectory.Size != 0)
            {
                rva = _managedNativeHeaderDirectory.RelativeVirtualAddress;
                isComposite = false;
                return true;
            }

            rva = 0;
            isComposite = false;
            return false;
        }

        public unsafe MetadataReader GetStandaloneAssemblyMetadata()
        {
            if (_corHeaderMetadataDirectory.Size == 0)
                return null;

            int metadataOffset = GetOffset(_corHeaderMetadataDirectory.RelativeVirtualAddress);
            return GetManifestAssemblyMetadata(metadataOffset, _corHeaderMetadataDirectory.Size);
        }

        public unsafe MetadataReader GetManifestAssemblyMetadata(int offset, int size)
        {
            // offset is an absolute file offset into the (already pinned) image bytes; it has had
            // _webcilOffset applied already by GetOffset, so we must not add it again here.
            if (size <= 0 || offset < 0 || (long)offset + size > _image.Length)
                throw new BadImageFormatException("Metadata range is outside the image");

            return new MetadataReader((byte*)Unsafe.AsPointer(ref _image[offset]), size);
        }

        /// <summary>
        /// Webcil images do not have a PE ImageBase, so VA-to-offset conversion
        /// is not supported. Always returns <see langword="false"/>.
        /// </summary>
        public bool TryGetFileOffsetFromImageVA(long imageVA, out int fileOffset)
        {
            fileOffset = 0;
            return false;
        }

        private void ReadCorHeader(out CorFlags flags, out DirectoryEntry metadataDirectory, out DirectoryEntry managedNativeHeaderDirectory)
        {
            ReadOnlySpan<byte> image = _image;
            int corHeaderOffset = GetOffset((int)_header.PeCliHeaderRva);

            // CorHeader layout:
            // int32  cb (byte count)
            // uint16 MajorRuntimeVersion
            // uint16 MinorRuntimeVersion
            // DirectoryEntry MetaData (RVA + Size)
            // uint32 Flags
            // int32  EntryPointTokenOrRelativeVirtualAddress
            // DirectoryEntry Resources
            // DirectoryEntry StrongNameSignature
            // DirectoryEntry CodeManagerTable
            // DirectoryEntry VTableFixups
            // DirectoryEntry ExportAddressTableJumps
            // DirectoryEntry ManagedNativeHeader (RVA + Size)
            const int CorHeaderSize = 72;
            if ((long)corHeaderOffset + CorHeaderSize > image.Length)
                throw new BadImageFormatException("COR header extends beyond the image");

            int offset = corHeaderOffset;
            offset += 4; // cb
            offset += 2; // MajorRuntimeVersion
            offset += 2; // MinorRuntimeVersion

            int metadataRva = BinaryPrimitives.ReadInt32LittleEndian(image.Slice(offset)); offset += 4;
            int metadataSize = BinaryPrimitives.ReadInt32LittleEndian(image.Slice(offset)); offset += 4;
            metadataDirectory = new DirectoryEntry(metadataRva, metadataSize);

            flags = (CorFlags)BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(offset)); offset += 4;

            offset += 4; // EntryPointTokenOrRelativeVirtualAddress
            offset += 8; // Resources
            offset += 8; // StrongNameSignature
            offset += 8; // CodeManagerTable
            offset += 8; // VTableFixups
            offset += 8; // ExportAddressTableJumps

            int managedNativeRva = BinaryPrimitives.ReadInt32LittleEndian(image.Slice(offset)); offset += 4;
            int managedNativeSize = BinaryPrimitives.ReadInt32LittleEndian(image.Slice(offset));
            managedNativeHeaderDirectory = new DirectoryEntry(managedNativeRva, managedNativeSize);
        }

        private static bool TryReadHeader(byte[] image, long offset, out WebcilHeader header)
        {
            header = default;

            if (offset < 0 || offset + WebcilConstants.V0HeaderSize > image.Length)
                return false;

            ReadOnlySpan<byte> span = image.AsSpan((int)offset);
            header.Id = BinaryPrimitives.ReadUInt32LittleEndian(span);
            header.VersionMajor = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(4));
            header.VersionMinor = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(6));
            header.CoffSections = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(8));
            // span[10..12] is Reserved0
            header.PeCliHeaderRva = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12));
            header.PeCliHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(16));
            header.PeDebugRva = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(20));
            header.PeDebugSize = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(24));

            if (header.Id != WebcilConstants.WEBCIL_MAGIC)
                return false;

            if (header.VersionMajor != 0 && header.VersionMajor != 1)
                return false;

            if (header.VersionMinor != WebcilConstants.WC_VERSION_MINOR)
                return false;

            if (header.VersionMajor >= 1)
            {
                if (offset + WebcilConstants.V1HeaderSize > image.Length)
                    return false;

                header.TableBase = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(WebcilConstants.V0HeaderSize));
            }
            else
            {
                header.TableBase = uint.MaxValue;
            }

            return true;
        }

        private static WebcilSectionHeader[] ReadSections(byte[] image, long webcilOffset, WebcilHeader header)
        {
            int headerSize = header.VersionMajor >= 1 ? WebcilConstants.V1HeaderSize : WebcilConstants.V0HeaderSize;
            long sectionDirectoryOffset = webcilOffset + headerSize;
            long sectionTableEnd = sectionDirectoryOffset + (long)header.CoffSections * WebcilConstants.SectionHeaderSize;
            if (sectionTableEnd > image.Length)
                throw new BadImageFormatException("Webcil section table extends beyond the image");

            var sections = new WebcilSectionHeader[header.CoffSections];
            for (int i = 0; i < header.CoffSections; i++)
            {
                ReadOnlySpan<byte> span = image.AsSpan((int)(sectionDirectoryOffset + (i * WebcilConstants.SectionHeaderSize)));
                sections[i] = new WebcilSectionHeader(
                    virtualSize: BinaryPrimitives.ReadUInt32LittleEndian(span),
                    virtualAddress: BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4)),
                    sizeOfRawData: BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8)),
                    pointerToRawData: BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12)));
            }

            return sections;
        }

        private static bool IsWasmModule(byte[] image)
        {
            // WASM magic: '\0asm'
            return image.Length >= 4
                && image[0] == 0x00
                && image[1] == 0x61
                && image[2] == 0x73
                && image[3] == 0x6D;
        }

        private static bool TryFindWebcilInWasm(byte[] image, out long webcilOffset)
        {
            webcilOffset = 0;

            // Parse the WASM module structure to find the data section (id == 11) which contains the
            // Webcil payload as a passive data segment whose bytes start with the Webcil magic.
            int offset = 8; // Skip WASM magic + version
            while (offset < image.Length)
            {
                byte sectionId = image[offset++];
                if (!TryReadLebU32(image, ref offset, image.Length, out uint sectionSize))
                    return false;

                long sectionEndLong = (long)offset + sectionSize;
                if (sectionEndLong > image.Length)
                    return false;
                int sectionEnd = (int)sectionEndLong;

                if (sectionId == 11) // Data section
                {
                    if (!TryReadLebU32(image, ref offset, sectionEnd, out uint segmentCount))
                        return false;

                    for (uint i = 0; i < segmentCount && offset < sectionEnd; i++)
                    {
                        if (offset >= sectionEnd)
                            return false;

                        byte kind = image[offset++];
                        if (kind == 1) // Passive segment
                        {
                            if (!TryReadLebU32(image, ref offset, sectionEnd, out uint dataSize))
                                return false;

                            if (dataSize >= 4 && (long)offset + dataSize <= sectionEnd)
                            {
                                uint magic = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset));
                                if (magic == WebcilConstants.WEBCIL_MAGIC && TryReadHeader(image, offset, out _))
                                {
                                    webcilOffset = offset;
                                    return true;
                                }
                            }

                            offset += (int)dataSize;
                        }
                        else if (kind == 0) // Active segment (memory 0)
                        {
                            if (!TrySkipConstExpr(image, ref offset, sectionEnd)
                                || !TryReadLebU32(image, ref offset, sectionEnd, out uint dataSize))
                                return false;
                            offset += (int)dataSize;
                        }
                        else if (kind == 2) // Active segment (explicit memory index)
                        {
                            if (!TryReadLebU32(image, ref offset, sectionEnd, out _)
                                || !TrySkipConstExpr(image, ref offset, sectionEnd)
                                || !TryReadLebU32(image, ref offset, sectionEnd, out uint dataSize))
                                return false;
                            offset += (int)dataSize;
                        }
                        else
                        {
                            return false; // Unknown segment kind
                        }
                    }

                    return false;
                }

                offset = sectionEnd;
            }

            return false;
        }

        private static bool TrySkipConstExpr(byte[] data, ref int offset, int end)
        {
            // Skip a WASM constant expression (terminated by 0x0B = end).
            while (offset < end)
            {
                byte opcode = data[offset++];
                switch (opcode)
                {
                    case 0x0B: // end
                        return true;
                    case 0x41: // i32.const
                    case 0x42: // i64.const
                    case 0x23: // global.get
                    case 0xD2: // ref.func
                        if (!TryReadLebU32(data, ref offset, end, out _))
                            return false;
                        break;
                    case 0x43: // f32.const
                        offset += 4;
                        break;
                    case 0x44: // f64.const
                        offset += 8;
                        break;
                    case 0xD0: // ref.null
                        offset++; // reftype byte
                        break;
                    // Extended const expressions - arithmetic ops with no operands.
                    case 0x6A: // i32.add
                    case 0x6B: // i32.sub
                    case 0x6C: // i32.mul
                    case 0x7C: // i64.add
                    case 0x7D: // i64.sub
                    case 0x7E: // i64.mul
                        break;
                }
            }

            return false;
        }

        /// <summary>
        /// Reads an unsigned LEB128 value, returning false if it runs past <paramref name="end"/> or
        /// would not fit in a uint.
        /// </summary>
        private static bool TryReadLebU32(byte[] data, ref int offset, int end, out uint value)
        {
            value = 0;
            int shift = 0;
            while (offset < end)
            {
                byte b = data[offset++];
                if (shift == 28 && (b & 0xF0) != 0)
                {
                    // More than 32 bits encoded.
                    return false;
                }

                value |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    return true;

                shift += 7;
                if (shift >= 35)
                    return false;
            }

            return false;
        }
    }
}
