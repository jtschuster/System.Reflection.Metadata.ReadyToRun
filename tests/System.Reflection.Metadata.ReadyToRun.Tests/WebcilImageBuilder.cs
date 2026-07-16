// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using Internal.Runtime;

namespace System.Reflection.Metadata.ReadyToRun.Tests
{
    /// <summary>
    /// Builds minimal, hand-constructed Webcil ReadyToRun images for regression testing
    /// the <c>WebcilImageReader</c>. Produces both raw <c>.webcil</c> images and WASM-wrapped
    /// images (the Webcil payload embedded as a passive data segment in a WASM data section).
    /// </summary>
    internal static class WebcilImageBuilder
    {
        private const uint WebcilMagic = 0x4c496257; // 'WbIL'
        private const uint R2RSignature = 0x00525452; // 'RTR\0'

        // Webcil v1 header size (28-byte v0 header + 4-byte TableBase).
        private const int V0HeaderSize = 28;
        private const int V1HeaderSize = 32;
        private const int SectionHeaderSize = 16;
        private const int CorHeaderSize = 72;

        // Arbitrary virtual address for the single section that holds all content.
        private const uint SectionVirtualAddress = 0x2000;

        internal readonly struct R2RSectionSpec
        {
            public readonly ReadyToRunSectionType Type;
            public readonly byte[] Content;

            public R2RSectionSpec(ReadyToRunSectionType type, byte[] content)
            {
                Type = type;
                Content = content;
            }
        }

        // Virtual address of the second section when a two-section layout is requested.
        private const uint SecondSectionVirtualAddress = 0x4000;

        /// <summary>
        /// Builds a raw <c>.webcil</c> ReadyToRun image.
        /// </summary>
        /// <param name="r2rMajorVersion">R2R header major version.</param>
        /// <param name="r2rMinorVersion">R2R header minor version.</param>
        /// <param name="sections">R2R sections to embed.</param>
        /// <param name="metadata">Optional ECMA-335 metadata blob referenced by the COR header.</param>
        /// <param name="webcilVersionMajor">Webcil header major version (0 = 28-byte header, 1 = 32-byte header).</param>
        /// <param name="splitSections">
        /// When true, the COR header is placed in a first Webcil section and the R2R header / section
        /// blobs / metadata in a second section with a distinct virtual address and raw pointer. This
        /// exercises section-table selection and cross-section RVA translation.
        /// </param>
        public static byte[] BuildRawWebcil(
            ushort r2rMajorVersion,
            ushort r2rMinorVersion,
            IReadOnlyList<R2RSectionSpec> sections,
            byte[]? metadata = null,
            ushort webcilVersionMajor = 1,
            bool splitSections = false,
            uint r2rFlags = 0)
        {
            int r2rHeaderSize = 16 + sections.Count * 12;
            uint restVA = splitSections ? SecondSectionVirtualAddress : SectionVirtualAddress + CorHeaderSize;

            // ── Lay out the "rest" buffer (everything except the COR header) ──
            // [0]   R2R header (16 + nSections*12 bytes)
            // [..]  per-section content blobs
            // [..]  optional metadata blob (4-byte aligned)
            int r2rOffset = 0;
            var sectionContentOffsets = new int[sections.Count];
            int cursor = r2rOffset + r2rHeaderSize;
            for (int i = 0; i < sections.Count; i++)
            {
                sectionContentOffsets[i] = cursor;
                cursor += sections[i].Content.Length;
            }

            int metadataOffset = -1;
            if (metadata is not null)
            {
                cursor = Align(cursor, 4);
                metadataOffset = cursor;
                cursor += metadata.Length;
            }

            int restSize = cursor;
            var rest = new byte[restSize];

            // ── R2R header ─────────────────────────────────────────────────
            var r2r = rest.AsSpan(r2rOffset, r2rHeaderSize);
            BinaryPrimitives.WriteUInt32LittleEndian(r2r.Slice(0), R2RSignature);
            BinaryPrimitives.WriteUInt16LittleEndian(r2r.Slice(4), r2rMajorVersion);
            BinaryPrimitives.WriteUInt16LittleEndian(r2r.Slice(6), r2rMinorVersion);
            BinaryPrimitives.WriteUInt32LittleEndian(r2r.Slice(8), r2rFlags);           // Flags
            BinaryPrimitives.WriteInt32LittleEndian(r2r.Slice(12), sections.Count);     // nSections
            int entry = 16;
            for (int i = 0; i < sections.Count; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(r2r.Slice(entry + 0), (int)sections[i].Type);
                BinaryPrimitives.WriteInt32LittleEndian(r2r.Slice(entry + 4), (int)restVA + sectionContentOffsets[i]);
                BinaryPrimitives.WriteInt32LittleEndian(r2r.Slice(entry + 8), sections[i].Content.Length);
                entry += 12;
            }

            for (int i = 0; i < sections.Count; i++)
            {
                sections[i].Content.CopyTo(rest.AsSpan(sectionContentOffsets[i]));
            }
            if (metadata is not null)
            {
                metadata.CopyTo(rest.AsSpan(metadataOffset));
            }

            // ── COR header ─────────────────────────────────────────────────
            // int32  cb
            // uint16 MajorRuntimeVersion / uint16 MinorRuntimeVersion
            // DirectoryEntry MetaData (RVA + Size)
            // uint32 Flags
            // int32  EntryPointToken
            // DirectoryEntry Resources / StrongName / CodeManager / VTableFixups / ExportAddressTableJumps
            // DirectoryEntry ManagedNativeHeader (RVA + Size)
            var cor = new byte[CorHeaderSize];
            BinaryPrimitives.WriteInt32LittleEndian(cor.AsSpan(0), CorHeaderSize);
            BinaryPrimitives.WriteUInt16LittleEndian(cor.AsSpan(4), 2); // MajorRuntimeVersion
            BinaryPrimitives.WriteUInt16LittleEndian(cor.AsSpan(6), 5); // MinorRuntimeVersion
            if (metadata is not null)
            {
                BinaryPrimitives.WriteInt32LittleEndian(cor.AsSpan(8), (int)restVA + metadataOffset);
                BinaryPrimitives.WriteInt32LittleEndian(cor.AsSpan(12), metadata.Length);
            }
            BinaryPrimitives.WriteUInt32LittleEndian(cor.AsSpan(16), (uint)CorFlags.ILLibrary); // Flags
            BinaryPrimitives.WriteInt32LittleEndian(cor.AsSpan(64), (int)restVA + r2rOffset); // ManagedNativeHeader RVA
            BinaryPrimitives.WriteInt32LittleEndian(cor.AsSpan(68), r2rHeaderSize);           // ManagedNativeHeader Size

            // ── Assemble the Webcil file ───────────────────────────────────
            int headerSize = webcilVersionMajor >= 1 ? V1HeaderSize : V0HeaderSize;
            ushort coffSections = (ushort)(splitSections ? 2 : 1);
            int sectionTableStart = headerSize;
            int firstSectionFileStart = sectionTableStart + coffSections * SectionHeaderSize;

            int totalSize;
            byte[] image;
            if (!splitSections)
            {
                // Single section containing COR header followed by the rest.
                int payloadSize = CorHeaderSize + restSize;
                totalSize = firstSectionFileStart + payloadSize;
                image = new byte[totalSize];

                cor.CopyTo(image.AsSpan(firstSectionFileStart));
                rest.CopyTo(image.AsSpan(firstSectionFileStart + CorHeaderSize));

                WriteSectionHeader(image, sectionTableStart, virtualSize: payloadSize, virtualAddress: SectionVirtualAddress,
                    sizeOfRawData: payloadSize, pointerToRawData: firstSectionFileStart);
            }
            else
            {
                // Section 0: COR header.  Section 1: rest (at a higher virtual address).
                int section0FileStart = firstSectionFileStart;
                int section1FileStart = section0FileStart + CorHeaderSize;
                totalSize = section1FileStart + restSize;
                image = new byte[totalSize];

                cor.CopyTo(image.AsSpan(section0FileStart));
                rest.CopyTo(image.AsSpan(section1FileStart));

                WriteSectionHeader(image, sectionTableStart, virtualSize: CorHeaderSize, virtualAddress: SectionVirtualAddress,
                    sizeOfRawData: CorHeaderSize, pointerToRawData: section0FileStart);
                WriteSectionHeader(image, sectionTableStart + SectionHeaderSize, virtualSize: restSize, virtualAddress: SecondSectionVirtualAddress,
                    sizeOfRawData: restSize, pointerToRawData: section1FileStart);
            }

            // ── Webcil header ──────────────────────────────────────────────
            var hdr = image.AsSpan(0, headerSize);
            BinaryPrimitives.WriteUInt32LittleEndian(hdr.Slice(0), WebcilMagic);        // Id
            BinaryPrimitives.WriteUInt16LittleEndian(hdr.Slice(4), webcilVersionMajor); // VersionMajor
            BinaryPrimitives.WriteUInt16LittleEndian(hdr.Slice(6), 0);                  // VersionMinor
            BinaryPrimitives.WriteUInt16LittleEndian(hdr.Slice(8), coffSections);       // CoffSections
            // offset 10..12: Reserved0 (0)
            BinaryPrimitives.WriteUInt32LittleEndian(hdr.Slice(12), SectionVirtualAddress); // PeCliHeaderRva (COR header is at the start of section 0)
            BinaryPrimitives.WriteUInt32LittleEndian(hdr.Slice(16), CorHeaderSize);         // PeCliHeaderSize
            // offset 20..24: PeDebugRva (0)
            // offset 24..28: PeDebugSize (0)
            if (webcilVersionMajor >= 1)
                BinaryPrimitives.WriteUInt32LittleEndian(hdr.Slice(28), 0); // TableBase

            return image;
        }

        private static void WriteSectionHeader(byte[] image, int offset, int virtualSize, uint virtualAddress, int sizeOfRawData, int pointerToRawData)
        {
            var sec = image.AsSpan(offset, SectionHeaderSize);
            BinaryPrimitives.WriteUInt32LittleEndian(sec.Slice(0), (uint)virtualSize);
            BinaryPrimitives.WriteUInt32LittleEndian(sec.Slice(4), virtualAddress);
            BinaryPrimitives.WriteUInt32LittleEndian(sec.Slice(8), (uint)sizeOfRawData);
            BinaryPrimitives.WriteUInt32LittleEndian(sec.Slice(12), (uint)pointerToRawData);
        }

        /// <summary>
        /// Wraps a raw Webcil image inside a minimal WASM module as a passive data segment.
        /// </summary>
        public static byte[] WrapInWasm(byte[] webcil)
        {
            var file = new List<byte>();

            // WASM magic + version.
            file.AddRange(new byte[] { 0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00 });

            // Build the data-section payload: vec(segments) where the single segment is passive
            // (kind == 1), preceded by its byte length.
            var dataPayload = new List<byte>();
            WriteLeb(dataPayload, 1);           // segment count
            dataPayload.Add(0x01);              // kind = passive
            WriteLeb(dataPayload, (uint)webcil.Length);
            dataPayload.AddRange(webcil);

            // Data section: id (11) + size + payload.
            file.Add(11);
            WriteLeb(file, (uint)dataPayload.Count);
            file.AddRange(dataPayload);

            return file.ToArray();
        }

        /// <summary>
        /// Wraps a raw Webcil image inside a WASM module using a more realistic layout: a preceding
        /// non-data section, and a decoy passive data segment placed before the real Webcil segment.
        /// </summary>
        public static byte[] WrapInWasmWithDecoy(byte[] webcil)
        {
            var file = new List<byte>();
            file.AddRange(new byte[] { 0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00 });

            // Preceding "type" section (id 1) with a trivial payload; the scanner must skip it.
            var typePayload = new byte[] { 0x00 }; // vec(functype) with count 0
            file.Add(1);
            WriteLeb(file, (uint)typePayload.Length);
            file.AddRange(typePayload);

            // Data section (id 11) with two passive segments: a decoy first, then the Webcil payload.
            byte[] decoy = new byte[200]; // > 127 bytes -> multi-byte LEB length; not the Webcil magic
            for (int i = 0; i < decoy.Length; i++)
                decoy[i] = 0xCC;

            var dataPayload = new List<byte>();
            WriteLeb(dataPayload, 2);                       // segment count
            dataPayload.Add(0x01);                          // decoy: passive
            WriteLeb(dataPayload, (uint)decoy.Length);
            dataPayload.AddRange(decoy);
            dataPayload.Add(0x01);                          // webcil: passive
            WriteLeb(dataPayload, (uint)webcil.Length);
            dataPayload.AddRange(webcil);

            file.Add(11);
            WriteLeb(file, (uint)dataPayload.Count);
            file.AddRange(dataPayload);

            return file.ToArray();
        }

        /// <summary>
        /// Builds a WASM module whose data section contains only a non-Webcil passive segment.
        /// </summary>
        public static byte[] WrapInWasmWithoutWebcil()
        {
            var file = new List<byte>();
            file.AddRange(new byte[] { 0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00 });

            byte[] junk = new byte[16];
            for (int i = 0; i < junk.Length; i++)
                junk[i] = 0xAB;

            var dataPayload = new List<byte>();
            WriteLeb(dataPayload, 1);
            dataPayload.Add(0x01);
            WriteLeb(dataPayload, (uint)junk.Length);
            dataPayload.AddRange(junk);

            file.Add(11);
            WriteLeb(file, (uint)dataPayload.Count);
            file.AddRange(dataPayload);

            return file.ToArray();
        }

        /// <summary>
        /// Produces a CompilerIdentifier section blob (UTF-8 string + null terminator).
        /// </summary>
        public static byte[] CompilerIdentifierContent(string identifier)
        {
            byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(identifier);
            var content = new byte[utf8.Length + 1];
            utf8.CopyTo(content, 0);
            content[utf8.Length] = 0; // null terminator (GetCompilerIdentifier reads Size - 1 bytes)
            return content;
        }

        /// <summary>
        /// Builds a minimal but valid ECMA-335 metadata blob containing a single assembly and module row.
        /// </summary>
        public static byte[] BuildMetadata(string assemblyName)
        {
            var mb = new MetadataBuilder();
            mb.AddModule(
                generation: 0,
                moduleName: mb.GetOrAddString(assemblyName + ".dll"),
                mvid: mb.GetOrAddGuid(Guid.NewGuid()),
                encId: default,
                encBaseId: default);
            mb.AddAssembly(
                name: mb.GetOrAddString(assemblyName),
                version: new Version(1, 0, 0, 0),
                culture: default,
                publicKey: default,
                flags: 0,
                hashAlgorithm: AssemblyHashAlgorithm.None);

            var root = new MetadataRootBuilder(mb);
            var blob = new BlobBuilder();
            root.Serialize(blob, methodBodyStreamRva: 0, mappedFieldDataStreamRva: 0);
            return blob.ToArray();
        }

        private static int Align(int value, int alignment) => (value + alignment - 1) & ~(alignment - 1);

        private static void WriteLeb(List<byte> output, uint value)
        {
            do
            {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if (value != 0)
                    b |= 0x80;
                output.Add(b);
            }
            while (value != 0);
        }
    }
}
