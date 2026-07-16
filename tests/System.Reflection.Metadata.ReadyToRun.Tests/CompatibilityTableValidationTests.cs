// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers.Binary;
using System.Reflection.Metadata.ReadyToRun.Webcil;

using Internal.ReadyToRunConstants;
using Internal.Runtime;

using Xunit;

namespace System.Reflection.Metadata.ReadyToRun.Tests;

public sealed class CompatibilityTableValidationTests
{
    public static TheoryData<ReadyToRunSectionType, byte[]> MisalignedFixedTables => new()
    {
        { ReadyToRunSectionType.RuntimeFunctions, new byte[1] },
        { ReadyToRunSectionType.HotColdMap, new byte[1] },
        { ReadyToRunSectionType.ComponentAssemblies, new byte[1] },
        { ReadyToRunSectionType.ExceptionInfo, new byte[1] },
        { ReadyToRunSectionType.ImportSections, new byte[1] },
    };

    public static TheoryData<ReadyToRunSectionType, byte[]> MalformedCountedTables => new()
    {
        { ReadyToRunSectionType.EnclosingTypeMap, new byte[] { 1, 0 } },
        { ReadyToRunSectionType.MethodIsGenericMap, Int32Bytes(-1) },
        { ReadyToRunSectionType.MethodIsGenericMap, Combine(Int32Bytes(9), new byte[1]) },
        { ReadyToRunSectionType.TypeGenericInfoMap, Int32Bytes(-1) },
        { ReadyToRunSectionType.TypeGenericInfoMap, Combine(Int32Bytes(3), new byte[1]) },
    };

    [Fact]
    public void UnknownMajorVersion_GatesEveryTypedSectionDecoder()
    {
        using ReadyToRunReader reader = CreateReader(
            majorVersion: 25,
            minorVersion: 0,
            ReadyToRunSectionType.CompilerIdentifier,
            new byte[] { 0 });
        ReadyToRunSection section = Assert.Single(reader.GetSections());

        Action[] typedOperations =
        [
            () => reader.GetCompilerIdentifier(section),
            () => reader.GetOwnerCompositeExecutable(section),
            () => reader.GetManifestMetadataReader(section),
            () => reader.GetRuntimeFunctionsTable(section),
            () => reader.GetHotColdMapTable(section),
            () => reader.GetComponentAssembliesTable(section),
            () => reader.GetExceptionInfoTable(section),
            () => reader.GetImportSectionsTableSection(section),
            () => reader.GetEnclosingTypeMapTable(section),
            () => reader.GetMethodIsGenericMapTable(section),
            () => reader.GetTypeGenericInfoMapTable(section),
            () => reader.GetMethodDefEntryPointsTable(section),
            () => reader.GetDebugInfoTable(section),
            () => reader.GetInliningInfoTable(section),
            () => reader.GetInliningInfo2Table(section),
            () => reader.GetCrossModuleInlineInfoTable(section),
            () => reader.GetAvailableTypesTable(section),
            () => reader.GetPgoInstrumentationDataTable(section),
            () => reader.GetInstanceMethodEntryPointsTable(section),
            () => reader.GetRuntimeFunctionIndexFromOffset(0, out _, out _),
            () => reader.DecodeFixupSignature(0),
            () => reader.GetUnwindInfo((UnwindInfoRva)1),
            () => reader.GetGcInfo((UnwindInfoRva)1),
            () => reader.GetDebugInfo((DebugInfoOffset)1),
            () => reader.GetEHInfo((EHInfoRva)1, 1),
            () => reader.GetSignatureTable((SignatureTableRva)1, 1),
            () => reader.GetGCRefMapTable((AuxiliaryDataTableRva)1, 1),
            () => _ = reader.ComponentAssemblyIndexOffset,
        ];

        foreach (Action operation in typedOperations)
            Assert.Throws<NotSupportedException>(operation);
    }

    [Fact]
    public void TypedSectionDecoder_RejectsWrongSectionType()
    {
        using ReadyToRunReader reader = CreateReader(
            majorVersion: 24,
            minorVersion: 0,
            ReadyToRunSectionType.CompilerIdentifier,
            Array.Empty<byte>());
        ReadyToRunSection section = Assert.Single(reader.GetSections());

        Assert.Throws<ArgumentException>(() => reader.GetRuntimeFunctionsTable(section));
    }

    [Theory]
    [MemberData(nameof(MisalignedFixedTables))]
    public void FixedRecordTable_RejectsPartialRecord(
        ReadyToRunSectionType sectionType,
        byte[] content)
    {
        using ReadyToRunReader reader = CreateReader(24, 0, sectionType, content);
        ReadyToRunSection section = Assert.Single(reader.GetSections());

        Assert.Throws<BadImageFormatException>(() => ParseTable(reader, section));
    }

    [Theory]
    [MemberData(nameof(MalformedCountedTables))]
    public void CountedTable_RejectsInvalidCountOrLength(
        ReadyToRunSectionType sectionType,
        byte[] content)
    {
        using ReadyToRunReader reader = CreateReader(24, 0, sectionType, content);
        ReadyToRunSection section = Assert.Single(reader.GetSections());

        Assert.Throws<BadImageFormatException>(() => ParseTable(reader, section));
    }

    [Fact]
    public void ExceptionInfo_RequiresTerminalSentinel()
    {
        using ReadyToRunReader reader = CreateReader(
            24,
            0,
            ReadyToRunSectionType.ExceptionInfo,
            new byte[2 * sizeof(int)]);
        ReadyToRunSection section = Assert.Single(reader.GetSections());

        Assert.Throws<BadImageFormatException>(() => reader.GetExceptionInfoTable(section));
    }

    [Fact]
    public void ImportDescriptor_UnknownDiscriminantsAreToleratedButRejectedStrictly()
    {
        byte[] descriptor = CreateImportDescriptor(
            sectionSize: 8,
            flags: (ReadyToRunImportSectionFlags)0x8000,
            type: (ReadyToRunImportSectionType)0xFE,
            encodedEntrySize: 4);

        using (ReadyToRunReader tolerantReader = CreateReader(
            24,
            0,
            ReadyToRunSectionType.ImportSections,
            descriptor))
        {
            ImportSectionEntry entry = Assert.Single(
                tolerantReader.GetImportSectionsTableSection(
                    Assert.Single(tolerantReader.GetSections())).Entries);
            Assert.Equal((ReadyToRunImportSectionFlags)0x8000, entry.Flags);
            Assert.Equal((ReadyToRunImportSectionType)0xFE, entry.Type);
        }

        using ReadyToRunReader strictReader = CreateReader(
            24,
            0,
            ReadyToRunSectionType.ImportSections,
            descriptor,
            ReadyToRunValidationMode.Strict);
        Assert.Throws<NotSupportedException>(
            () => strictReader.GetImportSectionsTableSection(
                Assert.Single(strictReader.GetSections())));
    }

    [Fact]
    public void ImportDescriptor_ZeroEntrySizeUsesTargetPointerSize()
    {
        byte[] descriptor = CreateImportDescriptor(
            sectionSize: 2 * sizeof(int),
            flags: ReadyToRunImportSectionFlags.None,
            type: ReadyToRunImportSectionType.Unknown,
            encodedEntrySize: 0);
        using ReadyToRunReader reader = CreateReader(
            24,
            0,
            ReadyToRunSectionType.ImportSections,
            descriptor);
        ImportSectionEntry entry = Assert.Single(
            reader.GetImportSectionsTableSection(
                Assert.Single(reader.GetSections())).Entries);

        Assert.Equal(reader.TargetPointerSize, reader.GetImportSectionEntrySize(entry));
        Assert.Equal(2 * sizeof(int) / reader.TargetPointerSize, reader.GetImportSectionEntryCount(entry));
    }

    [Fact]
    public void ImportDescriptor_RejectsNonDivisibleSlotTable()
    {
        byte[] descriptor = CreateImportDescriptor(
            sectionSize: 3,
            flags: ReadyToRunImportSectionFlags.None,
            type: ReadyToRunImportSectionType.Unknown,
            encodedEntrySize: 2);
        using ReadyToRunReader reader = CreateReader(
            24,
            0,
            ReadyToRunSectionType.ImportSections,
            descriptor);
        ImportSectionEntry entry = Assert.Single(
            reader.GetImportSectionsTableSection(
                Assert.Single(reader.GetSections())).Entries);

        Assert.Throws<BadImageFormatException>(() => reader.GetImportSectionEntryCount(entry));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x00 })]
    [InlineData(new byte[] { 0x03, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x00, 0x04, 0x03 })]
    [InlineData(new byte[] { 0x00, 0x02, 0x0A })]
    public void NativeHashtable_RejectsTruncatedOrInvalidBucketIndex(byte[] content)
    {
        using ReadyToRunReader reader = CreateReader(
            24,
            0,
            ReadyToRunSectionType.AvailableTypes,
            content);
        ReadyToRunSection section = Assert.Single(reader.GetSections());

        Assert.Throws<BadImageFormatException>(() => reader.GetAvailableTypesTable(section));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x06 })]
    [InlineData(new byte[] { 0x80 })]
    [InlineData(new byte[] { 0x08, 0x0A })]
    public void NativeArray_RejectsTruncatedOrInvalidIndex(byte[] content)
    {
        using ReadyToRunReader reader = CreateReader(
            24,
            0,
            ReadyToRunSectionType.DebugInfo,
            content);
        ReadyToRunSection section = Assert.Single(reader.GetSections());

        Assert.Throws<BadImageFormatException>(() => reader.GetDebugInfoTable(section));
    }

    [Fact]
    public void SignatureTableCache_IncludesEntryCount()
    {
        byte[] signatures = new byte[2 * sizeof(int)];
        BinaryPrimitives.WriteUInt32LittleEndian(signatures, 0x11111111);
        BinaryPrimitives.WriteUInt32LittleEndian(signatures.AsSpan(sizeof(int)), 0x22222222);
        using ReadyToRunReader reader = CreateReader(
            24,
            0,
            ReadyToRunSectionType.CompilerIdentifier,
            signatures);
        ReadyToRunSection section = Assert.Single(reader.GetSections());
        var handle = (SignatureTableRva)(uint)section.RelativeVirtualAddress;

        SignatureTable oneEntry = reader.GetSignatureTable(handle, 1);
        SignatureTable twoEntries = reader.GetSignatureTable(handle, 2);

        Assert.Single(oneEntry.Entries);
        Assert.Equal(2, twoEntries.Entries.Count);
    }

    [Fact]
    public void InliningInfo_RejectsOverflowingInlineeIndexSize()
    {
        using ReadyToRunReader reader = CreateReader(
            24,
            0,
            ReadyToRunSectionType.InliningInfo,
            Int32Bytes(0x7FFFFFF8));
        ReadyToRunSection section = Assert.Single(reader.GetSections());

        Assert.Throws<BadImageFormatException>(() => reader.GetInliningInfoTable(section));
    }

    [Fact]
    public void InliningInfo_RejectsCountLargerThanNibbleStream()
    {
        byte[] encodedIntMaxValue = [0xF9, 0xFF, 0xFF, 0xFF, 0xFF, 0x07];
        byte[] content = Combine(
            Int32Bytes(2 * sizeof(int)),
            Int32Bytes(1),
            Int32Bytes(0),
            encodedIntMaxValue);
        using ReadyToRunReader reader = CreateReader(
            24,
            0,
            ReadyToRunSectionType.InliningInfo,
            content);
        InliningInfoEntry entry = Assert.Single(
            reader.GetInliningInfoTable(Assert.Single(reader.GetSections())).Entries);

        Assert.Throws<BadImageFormatException>(() => reader.GetInliners(entry.InlinersOffset));
    }

    [Fact]
    public void DebugInfoPayload_CannotConsumeBytesPastItsSection()
    {
        byte[] debugInfoSection =
        [
            0x08, // NativeArray: one element, one-byte block index
            0x01, // First tree node is one byte after the index base
            0x02, 0x02, 0x02, 0x02, // Four left branches to element zero
            0x00, // Inline debug-info marker with no remaining payload bytes
        ];
        byte[] image = WebcilImageBuilder.BuildRawWebcil(
            24,
            0,
            [
                new WebcilImageBuilder.R2RSectionSpec(ReadyToRunSectionType.DebugInfo, debugInfoSection),
                new WebcilImageBuilder.R2RSectionSpec(ReadyToRunSectionType.ManifestMetadata, new byte[8]),
            ]);
        using var reader = new ReadyToRunReader(
            new WebcilImageReader(image),
            new NativeReader(new MemoryStream(image)));
        ReadyToRunSection debugSection = reader.GetSections().Single(
            section => section.Type == ReadyToRunSectionType.DebugInfo);
        DebugInfoEntry entry = Assert.Single(reader.GetDebugInfoTable(debugSection).Entries);

        Assert.Throws<BadImageFormatException>(() => reader.GetDebugInfo(entry.DebugInfoOffset));
    }

    [Fact]
    public void GCRefMap_AccountsForReservedStrideBoundaryOffset()
    {
        byte[] auxiliaryData = new byte[2 * sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(auxiliaryData, sizeof(int));
        using ReadyToRunReader reader = CreateReader(
            24,
            0,
            ReadyToRunSectionType.CompilerIdentifier,
            auxiliaryData);
        ReadyToRunSection section = Assert.Single(reader.GetSections());
        var handle = (AuxiliaryDataTableRva)(uint)section.RelativeVirtualAddress;

        Assert.Throws<BadImageFormatException>(() => reader.GetGCRefMapTable(handle, 1024));
    }

    private static void ParseTable(ReadyToRunReader reader, ReadyToRunSection section)
    {
        switch (section.Type)
        {
            case ReadyToRunSectionType.RuntimeFunctions:
                reader.GetRuntimeFunctionsTable(section);
                break;
            case ReadyToRunSectionType.HotColdMap:
                reader.GetHotColdMapTable(section);
                break;
            case ReadyToRunSectionType.ComponentAssemblies:
                reader.GetComponentAssembliesTable(section);
                break;
            case ReadyToRunSectionType.ExceptionInfo:
                reader.GetExceptionInfoTable(section);
                break;
            case ReadyToRunSectionType.ImportSections:
                reader.GetImportSectionsTableSection(section);
                break;
            case ReadyToRunSectionType.EnclosingTypeMap:
                reader.GetEnclosingTypeMapTable(section);
                break;
            case ReadyToRunSectionType.MethodIsGenericMap:
                reader.GetMethodIsGenericMapTable(section);
                break;
            case ReadyToRunSectionType.TypeGenericInfoMap:
                reader.GetTypeGenericInfoMapTable(section);
                break;
            default:
                throw new InvalidOperationException($"No parser registered for {section.Type}.");
        }
    }

    private static byte[] CreateImportDescriptor(
        int sectionSize,
        ReadyToRunImportSectionFlags flags,
        ReadyToRunImportSectionType type,
        byte encodedEntrySize)
    {
        byte[] descriptor = new byte[5 * sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(descriptor.AsSpan(sizeof(int)), sectionSize);
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(2 * sizeof(int)), (ushort)flags);
        descriptor[(2 * sizeof(int)) + sizeof(ushort)] = (byte)type;
        descriptor[(2 * sizeof(int)) + sizeof(ushort) + sizeof(byte)] = encodedEntrySize;
        return descriptor;
    }

    private static ReadyToRunReader CreateReader(
        ushort majorVersion,
        ushort minorVersion,
        ReadyToRunSectionType sectionType,
        byte[] content,
        ReadyToRunValidationMode validationMode = ReadyToRunValidationMode.Tolerant)
    {
        byte[] image = WebcilImageBuilder.BuildRawWebcil(
            majorVersion,
            minorVersion,
            [new WebcilImageBuilder.R2RSectionSpec(sectionType, content)]);
        return new ReadyToRunReader(
            new WebcilImageReader(image),
            new NativeReader(new MemoryStream(image)),
            options: new ReadyToRunReaderOptions(validationMode));
    }

    private static byte[] Int32Bytes(int value)
    {
        byte[] bytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] Combine(params byte[][] segments)
    {
        int length = segments.Sum(segment => segment.Length);
        byte[] result = new byte[length];
        int offset = 0;
        foreach (byte[] segment in segments)
        {
            segment.CopyTo(result, offset);
            offset += segment.Length;
        }
        return result;
    }
}
