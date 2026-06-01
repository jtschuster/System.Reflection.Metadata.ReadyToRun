// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.ReadyToRun;
using System.Reflection.Metadata.ReadyToRun.Webcil;
using System.Reflection.PortableExecutable;

using Internal.Runtime;

using Xunit;

namespace System.Reflection.Metadata.ReadyToRun.Tests
{
    /// <summary>
    /// Regression tests for Webcil / WASM ReadyToRun support. These build minimal, hand-constructed
    /// Webcil R2R images (raw and WASM-wrapped) and verify the structural reader parses them.
    /// </summary>
    public class WebcilReadyToRunTests
    {
        private const ushort R2RMajorVersion = 18;
        private const ushort R2RMinorVersion = 0;
        private const string CompilerId = "WebcilTestCompiler 1.0";

        private static IReadOnlyList<WebcilImageBuilder.R2RSectionSpec> SampleSections() => new[]
        {
            new WebcilImageBuilder.R2RSectionSpec(
                ReadyToRunSectionType.CompilerIdentifier,
                WebcilImageBuilder.CompilerIdentifierContent(CompilerId)),
            new WebcilImageBuilder.R2RSectionSpec(
                ReadyToRunSectionType.RuntimeFunctions,
                new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 }),
        };

        private static ReadyToRunReader CreateReader(byte[] image)
            => new ReadyToRunReader(new WebcilImageReader(image), new NativeReader(new MemoryStream(image)));

        [Fact]
        public void RawWebcil_IsDetectedAsWebcil()
        {
            byte[] image = WebcilImageBuilder.BuildRawWebcil(R2RMajorVersion, R2RMinorVersion, SampleSections());

            Assert.True(WebcilImageReader.IsWebcilImage(image));
            // A bare PE/garbage buffer must not be detected as Webcil.
            Assert.False(WebcilImageReader.IsWebcilImage(new byte[] { 0x4D, 0x5A, 0x90, 0x00 }));
        }

        [Fact]
        public void WasmWrapped_IsDetectedAsWebcil()
        {
            byte[] raw = WebcilImageBuilder.BuildRawWebcil(R2RMajorVersion, R2RMinorVersion, SampleSections());
            byte[] wasm = WebcilImageBuilder.WrapInWasm(raw);

            Assert.True(WebcilImageReader.IsWebcilImage(wasm));
        }

        [Fact]
        public void RawWebcil_ReportsWasmMachineAndPointerSize()
        {
            byte[] image = WebcilImageBuilder.BuildRawWebcil(R2RMajorVersion, R2RMinorVersion, SampleSections());
            using ReadyToRunReader reader = CreateReader(image);

            Assert.Equal(WasmMachine.Wasm32, reader.Machine);
            Assert.Equal(4, reader.TargetPointerSize);
        }

        [Fact]
        public void RawWebcil_ParsesReadyToRunHeader()
        {
            byte[] image = WebcilImageBuilder.BuildRawWebcil(R2RMajorVersion, R2RMinorVersion, SampleSections());
            using ReadyToRunReader reader = CreateReader(image);

            ReadyToRunHeader header = reader.ReadyToRunHeader;
            Assert.Equal(ReadyToRunHeader.READYTORUN_SIGNATURE, header.Signature);
            Assert.Equal(R2RMajorVersion, header.MajorVersion);
            Assert.Equal(R2RMinorVersion, header.MinorVersion);
            Assert.False(reader.Composite);
        }

        [Fact]
        public void RawWebcil_ParsesSectionsAndCompilerIdentifier()
        {
            byte[] image = WebcilImageBuilder.BuildRawWebcil(R2RMajorVersion, R2RMinorVersion, SampleSections());
            using ReadyToRunReader reader = CreateReader(image);

            IReadOnlyList<ReadyToRunSection> sections = reader.GetSections();
            Assert.Equal(2, sections.Count);

            ReadyToRunSection compilerSection = FindSection(sections, ReadyToRunSectionType.CompilerIdentifier);
            Assert.Equal(CompilerId.Length + 1, compilerSection.Size);
            Assert.Equal(CompilerId, reader.GetCompilerIdentifier(compilerSection));

            ReadyToRunSection runtimeFunctions = FindSection(sections, ReadyToRunSectionType.RuntimeFunctions);
            Assert.Equal(8, runtimeFunctions.Size);
        }

        [Fact]
        public void WasmWrapped_ParsesIdenticallyToRaw()
        {
            byte[] raw = WebcilImageBuilder.BuildRawWebcil(R2RMajorVersion, R2RMinorVersion, SampleSections());
            byte[] wasm = WebcilImageBuilder.WrapInWasm(raw);

            using ReadyToRunReader reader = CreateReader(wasm);
            var webcilReader = new WebcilImageReader(wasm);
            Assert.True(webcilReader.IsWasmWrapped);

            Assert.Equal(WasmMachine.Wasm32, reader.Machine);
            Assert.Equal(4, reader.TargetPointerSize);

            ReadyToRunHeader header = reader.ReadyToRunHeader;
            Assert.Equal(ReadyToRunHeader.READYTORUN_SIGNATURE, header.Signature);
            Assert.Equal(R2RMajorVersion, header.MajorVersion);

            ReadyToRunSection compilerSection = FindSection(reader.GetSections(), ReadyToRunSectionType.CompilerIdentifier);
            Assert.Equal(CompilerId, reader.GetCompilerIdentifier(compilerSection));

            webcilReader.Dispose();
        }

        [Fact]
        public void RawWebcil_WithMetadata_ReadsAssemblyName()
        {
            const string assemblyName = "WebcilSample";
            byte[] metadata = WebcilImageBuilder.BuildMetadata(assemblyName);
            byte[] image = WebcilImageBuilder.BuildRawWebcil(R2RMajorVersion, R2RMinorVersion, SampleSections(), metadata);

            using ReadyToRunReader reader = CreateReader(image);

            MetadataReader mdReader = reader.GetStandaloneMetadata();
            Assert.NotNull(mdReader);

            AssemblyDefinition asm = mdReader.GetAssemblyDefinition();
            Assert.Equal(assemblyName, mdReader.GetString(asm.Name));
        }

        [Fact]
        public void WasmWrapped_WithMetadata_ReadsAssemblyName()
        {
            const string assemblyName = "WebcilWasmSample";
            byte[] metadata = WebcilImageBuilder.BuildMetadata(assemblyName);
            byte[] raw = WebcilImageBuilder.BuildRawWebcil(R2RMajorVersion, R2RMinorVersion, SampleSections(), metadata);
            byte[] wasm = WebcilImageBuilder.WrapInWasm(raw);

            using ReadyToRunReader reader = CreateReader(wasm);

            MetadataReader mdReader = reader.GetStandaloneMetadata();
            Assert.NotNull(mdReader);

            AssemblyDefinition asm = mdReader.GetAssemblyDefinition();
            Assert.Equal(assemblyName, mdReader.GetString(asm.Name));
        }

        [Fact]
        public void RawWebcil_WithoutMetadata_ReturnsNullStandaloneMetadata()
        {
            byte[] image = WebcilImageBuilder.BuildRawWebcil(R2RMajorVersion, R2RMinorVersion, SampleSections());
            using ReadyToRunReader reader = CreateReader(image);

            Assert.Null(reader.GetStandaloneMetadata());
        }

        [Fact]
        public void RawWebcil_V0Header_Parses()
        {
            byte[] image = WebcilImageBuilder.BuildRawWebcil(
                R2RMajorVersion, R2RMinorVersion, SampleSections(), metadata: null, webcilVersionMajor: 0);
            using ReadyToRunReader reader = CreateReader(image);

            Assert.Equal(WasmMachine.Wasm32, reader.Machine);
            ReadyToRunHeader header = reader.ReadyToRunHeader;
            Assert.Equal(ReadyToRunHeader.READYTORUN_SIGNATURE, header.Signature);

            ReadyToRunSection compilerSection = FindSection(reader.GetSections(), ReadyToRunSectionType.CompilerIdentifier);
            Assert.Equal(CompilerId, reader.GetCompilerIdentifier(compilerSection));
        }

        [Fact]
        public void RawWebcil_TwoSections_TranslatesCrossSectionRvas()
        {
            const string assemblyName = "WebcilTwoSection";
            byte[] metadata = WebcilImageBuilder.BuildMetadata(assemblyName);
            // COR header lives in section 0; R2R header, blobs and metadata in section 1.
            byte[] image = WebcilImageBuilder.BuildRawWebcil(
                R2RMajorVersion, R2RMinorVersion, SampleSections(), metadata, webcilVersionMajor: 1, splitSections: true);

            using ReadyToRunReader reader = CreateReader(image);

            ReadyToRunSection compilerSection = FindSection(reader.GetSections(), ReadyToRunSectionType.CompilerIdentifier);
            Assert.Equal(CompilerId, reader.GetCompilerIdentifier(compilerSection));

            MetadataReader mdReader = reader.GetStandaloneMetadata();
            Assert.NotNull(mdReader);
            Assert.Equal(assemblyName, mdReader.GetString(mdReader.GetAssemblyDefinition().Name));
        }

        [Fact]
        public void RawWebcil_SectionRva_MapsToExactBytes()
        {
            byte[] runtimeFunctionBytes = { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
            byte[] image = WebcilImageBuilder.BuildRawWebcil(R2RMajorVersion, R2RMinorVersion, SampleSections());
            using ReadyToRunReader reader = CreateReader(image);

            ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.RuntimeFunctions);
            int offset = reader.GetOffsetForRVA(section.RelativeVirtualAddress);
            for (int i = 0; i < runtimeFunctionBytes.Length; i++)
            {
                Assert.Equal(runtimeFunctionBytes[i], reader.ImageReader.ReadByte(ref offset));
            }
        }

        [Fact]
        public void WasmWrapped_WithDecoySegment_FindsWebcilPayload()
        {
            byte[] raw = WebcilImageBuilder.BuildRawWebcil(R2RMajorVersion, R2RMinorVersion, SampleSections());
            byte[] wasm = WebcilImageBuilder.WrapInWasmWithDecoy(raw);

            Assert.True(WebcilImageReader.IsWebcilImage(wasm));

            using ReadyToRunReader reader = CreateReader(wasm);
            ReadyToRunSection compilerSection = FindSection(reader.GetSections(), ReadyToRunSectionType.CompilerIdentifier);
            Assert.Equal(CompilerId, reader.GetCompilerIdentifier(compilerSection));
        }

        [Fact]
        public void WasmWithoutWebcilPayload_IsNotDetectedAndThrows()
        {
            byte[] wasm = WebcilImageBuilder.WrapInWasmWithoutWebcil();

            Assert.False(WebcilImageReader.IsWebcilImage(wasm));
            Assert.Throws<BadImageFormatException>(() => new WebcilImageReader(wasm));
        }

        [Fact]
        public void TruncatedWebcilHeader_Throws()
        {
            // Starts with the Webcil magic (so the cheap detection check passes) but is too short
            // to contain a complete header, so construction must fail.
            byte[] truncated = { 0x57, 0x62, 0x49, 0x4C, 0x01, 0x00 };

            Assert.True(WebcilImageReader.IsWebcilImage(truncated));
            Assert.Throws<BadImageFormatException>(() => new WebcilImageReader(truncated));
        }

        [Fact]
        public void GetOffset_UnmappedRva_Throws()
        {
            byte[] image = WebcilImageBuilder.BuildRawWebcil(R2RMajorVersion, R2RMinorVersion, SampleSections());
            using var reader = new WebcilImageReader(image);

            // RVA 0 falls outside every section's virtual address range.
            Assert.Throws<BadImageFormatException>(() => reader.GetOffset(0));
        }

        private static ReadyToRunSection FindSection(IReadOnlyList<ReadyToRunSection> sections, ReadyToRunSectionType type)
        {
            foreach (ReadyToRunSection section in sections)
            {
                if (section.Type == type)
                    return section;
            }

            throw new Xunit.Sdk.XunitException($"Section {type} not found.");
        }
    }
}
