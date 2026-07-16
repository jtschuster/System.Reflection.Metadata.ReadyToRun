// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

using Internal.ReadyToRunConstants;
using Internal.Runtime;


namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// A low-level, structural reader for Ready-to-Run images. Parses headers and sections
    /// without cross-referencing metadata — each section table exposes only the raw indices,
    /// RIDs, offsets, and flags encoded in that section.
    /// Also implements <see cref="IR2RImageContext"/> to support signature decoding.
    /// </summary>
    public sealed partial class ReadyToRunReader : IR2RImageContext, IDisposable
    {
        private readonly IPlatformBinaryReader _platformBinaryReader;
        private readonly NativeReader _nativeReader;
        private readonly string _filename;
        private readonly ReadyToRunReaderOptions _options;

        // Lazy-init backing fields
        private ReadyToRunHeader _header;
        private ReadyToRunFormatProfile _formatProfile;
        private bool? _isComposite;

        public ReadyToRunReader(
            IPlatformBinaryReader platformBinaryReader,
            NativeReader nativeReader,
            string filename = null,
            ReadyToRunReaderOptions options = null)
        {
            _platformBinaryReader = platformBinaryReader ?? throw new ArgumentNullException(nameof(platformBinaryReader));
            _nativeReader = nativeReader ?? throw new ArgumentNullException(nameof(nativeReader));
            _filename = filename ?? string.Empty;
            _options = options ?? ReadyToRunReaderOptions.Default;
        }

        public void Dispose()
        {
            _nativeReader.Dispose();
            _platformBinaryReader.Dispose();
        }

        /// <summary>NativeReader for raw byte access into the image.</summary>
        public NativeReader ImageReader => _nativeReader;

        /// <summary>Filename of the R2R image being read.</summary>
        public string Filename => _filename;

        /// <summary>Validation policy used by this reader.</summary>
        public ReadyToRunValidationMode ValidationMode => _options.ValidationMode;

        /// <summary>Whether the parsed header version has a known semantic format profile.</summary>
        public ReadyToRunFormatSupport FormatSupport => FormatProfile.Support;

        internal ReadyToRunFormatProfile FormatProfile
        {
            get
            {
                if (_formatProfile is null)
                    GetHeader();
                return _formatProfile;
            }
        }

        internal ReadyToRunSignatureDecodingOptions SignatureDecodingOptions
        {
            get
            {
                ReadyToRunHeader header = GetHeader();
                EnsureSemanticDecodingSupported("ReadyToRun signature decoding");
                return ReadyToRunSignatureCompatibility.FromHeader(header, ValidationMode);
            }
        }

        /// <summary>
        /// Whether component assembly indices in the manifest start at 2 (V6+).
        /// In older formats they start at 1.
        /// </summary>
        public bool ComponentAssemblyIndicesStartAtTwo
        {
            get
            {
                EnsureSemanticDecodingSupported(nameof(ComponentAssemblyIndicesStartAtTwo));
                return FormatProfile.ComponentAssemblyIndexOffset == 2;
            }
        }

        /// <summary>Offset used for component assembly index adjustment.</summary>
        public int ComponentAssemblyIndexOffset => ComponentAssemblyIndicesStartAtTwo ? 2 : 1;

        /// <summary>Machine architecture of the image.</summary>
        public Machine Machine
        {
            get
            {
                return _platformBinaryReader.Machine;
            }
        }

        /// <summary>Pointer size for the target architecture (4 or 8).</summary>
        public int TargetPointerSize
        {
            get
            {
                return Machine switch
                {
                    Machine.I386 or Machine.Arm or Machine.Thumb or Machine.ArmThumb2 => 4,
                    Machine.Amd64 or Machine.Arm64 or Machine.LoongArch64 or Machine.RiscV64 => 8,
                    WasmMachine.Wasm32 => 4,
                    _ => throw new NotImplementedException(Machine.ToString()),
                };
            }
        }

        /// <summary>Whether this is a composite R2R image.</summary>
        public bool Composite
        {
            get
            {
                if (_isComposite.HasValue)
                    return _isComposite.Value;

                if (!_platformBinaryReader.TryGetReadyToRunHeader(out _, out bool isComposite))
                    throw new BadImageFormatException("Image is not a ReadyToRunImage");
                _isComposite = isComposite;

                return _isComposite.Value;
            }
        }

        /// <summary>The parsed R2R header.</summary>
        public ReadyToRunHeader ReadyToRunHeader
        {
            get
            {
                return GetHeader();
            }
        }

        /// <summary>Get the file offset corresponding to an RVA.</summary>
        public int GetOffsetForRVA(int rva) => _platformBinaryReader.GetOffset(rva);

        /// <summary>Get the file offset corresponding to a section RVA.</summary>
        public int GetOffsetForRVA(ImageRVA rva) => _platformBinaryReader.GetOffset((int)rva);

        /// <summary>
        /// All section handles from the global R2R header.
        /// </summary>
        public IReadOnlyList<ReadyToRunSection> GetSections() => GetHeader().Sections;

        public ReadyToRunHeader GetHeader()
        {
            if (_header is not null)
                return _header;

            if (!_platformBinaryReader.TryGetReadyToRunHeader(out int headerRva, out bool isComposite))
                throw new BadImageFormatException("Not a ReadyToRun image");

            _isComposite = isComposite;
            int headerOffset = GetOffsetForRVA(headerRva);
            _header = ReadReadyToRunHeader(headerOffset);
            _formatProfile = ReadyToRunFormatProfile.Create(_header.MajorVersion, _header.MinorVersion);
            return _header;
        }

        /// <summary>
        /// Copies the exact bytes described by a section directory entry without interpreting the payload.
        /// This remains available for unknown future R2R versions in tolerant mode.
        /// </summary>
        public byte[] GetSectionBytes(ReadyToRunSection section)
        {
            int offset = ValidateAndGetSectionOffset(section);
            byte[] bytes = new byte[section.Size];
            try
            {
                _nativeReader.ReadSpanAt(ref offset, bytes);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new BadImageFormatException(
                    $"ReadyToRun section {(int)section.Type} extends outside the image.", exception);
            }
            catch (EndOfStreamException exception)
            {
                throw new BadImageFormatException(
                    $"ReadyToRun section {(int)section.Type} is truncated.", exception);
            }
            return bytes;
        }

        internal int ValidateAndGetSectionOffset(ReadyToRunSection section)
        {
            if (section.Size < 0)
                throw new BadImageFormatException($"ReadyToRun section {(int)section.Type} has a negative size.");

            if (section.Size == 0)
                return 0;

            uint rva = (uint)section.RelativeVirtualAddress;
            if (rva > int.MaxValue)
                throw new NotSupportedException($"ReadyToRun section RVA 0x{rva:X8} exceeds the reader's address range.");

            uint lastRva;
            try
            {
                lastRva = checked(rva + (uint)section.Size - 1);
            }
            catch (OverflowException exception)
            {
                throw new BadImageFormatException(
                    $"ReadyToRun section {(int)section.Type} RVA and size overflow.", exception);
            }

            if (lastRva > int.MaxValue)
                throw new NotSupportedException($"ReadyToRun section end RVA 0x{lastRva:X8} exceeds the reader's address range.");

            int startOffset = GetOffsetForRVA((int)rva);
            int endOffset = GetOffsetForRVA((int)lastRva);
            long expectedEndOffset = (long)startOffset + section.Size - 1;
            if (expectedEndOffset >= int.MaxValue)
            {
                throw new NotSupportedException(
                    $"ReadyToRun section {(int)section.Type} exceeds the reader's file-offset range.");
            }

            if (endOffset != expectedEndOffset || startOffset < 0 || expectedEndOffset >= _nativeReader.Length)
            {
                throw new BadImageFormatException(
                    $"ReadyToRun section {(int)section.Type} does not map to a contiguous in-image byte range.");
            }

            return startOffset;
        }

        internal int ValidateAndGetSectionOffset(
            ReadyToRunSection section,
            ReadyToRunSectionType expectedType,
            string operation)
        {
            EnsureSemanticDecodingSupported(operation);
            if (section.Type != expectedType)
            {
                throw new ArgumentException(
                    $"{operation} requires a {expectedType} section.",
                    nameof(section));
            }

            return ValidateAndGetSectionOffset(section);
        }

        internal int ValidateAndGetRvaRange(uint rva, int size, string operation)
        {
            if (size < 0)
                throw new BadImageFormatException($"{operation} has a negative byte length.");
            if (rva > int.MaxValue)
                throw new NotSupportedException($"{operation} RVA 0x{rva:X8} exceeds the reader's address range.");

            int startOffset = GetOffsetForRVA((int)rva);
            if (size == 0)
            {
                if (startOffset < 0 || startOffset > _nativeReader.Length)
                    throw new BadImageFormatException($"{operation} starts outside the image.");
                return startOffset;
            }

            uint lastRva;
            try
            {
                lastRva = checked(rva + (uint)size - 1);
            }
            catch (OverflowException exception)
            {
                throw new BadImageFormatException($"{operation} RVA and byte length overflow.", exception);
            }

            if (lastRva > int.MaxValue)
                throw new NotSupportedException($"{operation} end RVA 0x{lastRva:X8} exceeds the reader's address range.");

            int lastOffset = GetOffsetForRVA((int)lastRva);
            long expectedLastOffset = (long)startOffset + size - 1;
            if (expectedLastOffset >= int.MaxValue)
                throw new NotSupportedException($"{operation} exceeds the reader's file-offset range.");

            if (startOffset < 0 || lastOffset != expectedLastOffset || expectedLastOffset >= _nativeReader.Length)
                throw new BadImageFormatException($"{operation} does not map to a contiguous in-image byte range.");

            return startOffset;
        }

        internal void EnsureSemanticDecodingSupported(string operation)
            => FormatProfile.EnsureSemanticDecodingSupported(operation);

        /// <summary>Gets the compiler identifier string from a CompilerIdentifier section.</summary>
        public string GetCompilerIdentifier(ReadyToRunSection section)
        {
            EnsureSemanticDecodingSupported(nameof(GetCompilerIdentifier));
            return GetNullTerminatedUtf8String(
                section,
                Internal.Runtime.ReadyToRunSectionType.CompilerIdentifier,
                nameof(GetCompilerIdentifier),
                nullTerminated: false);
        }

        /// <summary>Gets the owner composite executable filename from an OwnerCompositeExecutable section.</summary>
        public string GetOwnerCompositeExecutable(ReadyToRunSection section)
        {
            EnsureSemanticDecodingSupported(nameof(GetOwnerCompositeExecutable));
            return GetNullTerminatedUtf8String(
                section,
                Internal.Runtime.ReadyToRunSectionType.OwnerCompositeExecutable,
                nameof(GetOwnerCompositeExecutable),
                nullTerminated: true);
        }

        private string GetNullTerminatedUtf8String(
            ReadyToRunSection section,
            Internal.Runtime.ReadyToRunSectionType expectedType,
            string operation,
            bool nullTerminated)
        {
            if (section.Type != expectedType)
                throw new ArgumentException($"{operation} requires a {expectedType} section.", nameof(section));

            byte[] bytes = GetSectionBytes(section);
            if (bytes.Length == 0)
                return string.Empty;
            if (nullTerminated && bytes[^1] != 0)
                throw new BadImageFormatException($"{expectedType} section is not null terminated.");

            int byteCount = bytes[^1] == 0 ? bytes.Length - 1 : bytes.Length;
            try
            {
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                    .GetString(bytes, 0, byteCount);
            }
            catch (DecoderFallbackException exception)
            {
                throw new BadImageFormatException($"{expectedType} section contains invalid UTF-8.", exception);
            }
        }

        /// <summary>
        /// Gets the standalone (component 0) metadata for non-composite images.
        /// Returns null for composite images.
        /// The ReadyToRunReader must remain alive and undisposed while the returned reader is in use.
        /// </summary>
        public MetadataReader GetStandaloneMetadata()
        {
            if (Composite)
                return null;
            return _platformBinaryReader.GetStandaloneAssemblyMetadata();
        }

        /// <summary>
        /// Returns a MetadataReader for the ManifestMetadata module.
        /// The ReadyToRunReader must remain alive and undisposed while the returned reader is in use.
        /// </summary>
        public MetadataReader GetManifestMetadataReader(ReadyToRunSection manifestSection)
        {
            int manifestOffset = ValidateAndGetSectionOffset(
                manifestSection,
                ReadyToRunSectionType.ManifestMetadata,
                nameof(GetManifestMetadataReader));
            int manifestSize = manifestSection.Size;
            if (manifestSize <= 0)
                return null;

            return _platformBinaryReader.GetManifestAssemblyMetadata(manifestOffset, manifestSize);
        }

        // ── Entry point descriptor decoding ────────────────────────────────

        /// <summary>
        /// Decode the runtime function index and optional fixup offset from a compressed
        /// entry point descriptor at the given image offset. This is the same encoding used
        /// after MethodDef and InstanceMethod signature blobs.
        /// </summary>
        /// <param name="offset">Image offset to start reading from.</param>
        /// <param name="runtimeFunctionIndex">Decoded runtime function index.</param>
        /// <param name="fixupOffset">Fixup list offset, or null if no fixups.</param>
        public void GetRuntimeFunctionIndexFromOffset(int offset, out int runtimeFunctionIndex, out int? fixupOffset)
        {
            EnsureSemanticDecodingSupported(nameof(GetRuntimeFunctionIndexFromOffset));
            fixupOffset = null;

            uint id = 0;
            offset = (int)_nativeReader.DecodeUnsigned((uint)offset, ref id);
            if ((id & 1) != 0)
            {
                if ((id & 2) != 0)
                {
                    uint val = 0;
                    _nativeReader.DecodeUnsigned((uint)offset, ref val);
                    offset -= (int)val;
                }

                fixupOffset = offset;
                id >>= 2;
            }
            else
            {
                id >>= 1;
            }

            runtimeFunctionIndex = (int)id;
        }

        /// <summary>
        /// Decode a fixup signature at the given RVA into an AST representation.
        /// The returned <see cref="R2RFixupSignature"/> contains the fixup kind,
        /// optional module override index, and a payload with raw token RIDs and
        /// module indices — no assembly resolution is performed.
        /// </summary>
        /// <param name="signatureRva">The RVA of the signature in the image.</param>
        /// <returns>The decoded fixup signature AST node.</returns>
        public R2RFixupSignature DecodeFixupSignature(int signatureRva)
            => DecodeFixupSignature(signatureRva, SignatureDecodingOptions);

        /// <summary>
        /// Decode a fixup signature using explicit ReadyToRun version and validation settings
        /// instead of the current image header and reader validation mode.
        /// </summary>
        public R2RFixupSignature DecodeFixupSignature(int signatureRva, IReadyToRunSignatureDecodingOptions options)
        {
            EnsureSemanticDecodingSupported(nameof(DecodeFixupSignature));
            int offset = _platformBinaryReader.GetOffset(signatureRva);
            R2RSignature signature = RawSignatureDecoder.DecodeFixupSignature(_nativeReader, offset, TargetPointerSize, options);
            return R2RFixupSignature.FromSignature(signature);
        }
    }
}
