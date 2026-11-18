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
            majorVersion: 35,
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
            () => reader.GetRuntimeFunctionIndexFromOffset(default, out _, out _),
            () => reader.DecodeFixupSignature(default(SignatureRva)),
            () => reader.GetUnwindInfo((UnwindInfoRva)1),
            () => reader.GetGcInfo((UnwindInfoRva)1),
            () => reader.GetDebugInfo(new DebugInfoTable(new NativeArrayHandle(0, 0), 1), (DebugInfoOffset)1),
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
    public void ExceptionInfo_ExposesTerminalSentinelForLastEntryLength()
    {
        byte[] content = Combine(
            Int32Bytes(0x1000), Int32Bytes(0x2000),
            Int32Bytes(0x1010), Int32Bytes(0x2018),
            Int32Bytes(-1), Int32Bytes(0x2048));
        using ReadyToRunReader reader = CreateReader(24, 0, ReadyToRunSectionType.ExceptionInfo, content);
        ReadyToRunSection section = Assert.Single(reader.GetSections());

        IReadOnlyList<ExceptionInfoEntry> entries = reader.GetExceptionInfoTable(section).Entries;

        Assert.Equal(3, entries.Count);
        Assert.Equal((CodeRva)0x1000, entries[0].MethodRva);
        Assert.Equal((EHInfoRva)0x2000, entries[0].EhInfoRva);
        Assert.Equal((CodeRva)0x1010, entries[1].MethodRva);
        Assert.Equal((EHInfoRva)0x2018, entries[1].EhInfoRva);
        Assert.Equal((CodeRva)uint.MaxValue, entries[2].MethodRva);
        Assert.Equal((EHInfoRva)0x2048, entries[2].EhInfoRva);
        Assert.Equal(48u, (uint)entries[2].EhInfoRva - (uint)entries[1].EhInfoRva);
    }

    [Fact]
    public void ExceptionInfo_SentinelOnlyTableExposesSentinel()
    {
        byte[] content = Combine(Int32Bytes(-1), Int32Bytes(0x2000));
        using ReadyToRunReader reader = CreateReader(24, 0, ReadyToRunSectionType.ExceptionInfo, content);
        ReadyToRunSection section = Assert.Single(reader.GetSections());

        ExceptionInfoEntry sentinel = Assert.Single(reader.GetExceptionInfoTable(section).Entries);

        Assert.Equal((CodeRva)uint.MaxValue, sentinel.MethodRva);
        Assert.Equal((EHInfoRva)0x2000, sentinel.EhInfoRva);
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

        // A malformed header is rejected when the table opens; a bad bucket index is rejected on enumeration.
        Assert.Throws<BadImageFormatException>(
            () => reader.EnumerateAvailableTypeEntries(reader.GetAvailableTypesTable(section)).ToList());
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

        // A malformed header is rejected when the table opens; a bad block index is rejected on enumeration.
        Assert.Throws<BadImageFormatException>(
            () => reader.EnumerateDebugInfoEntries(reader.GetDebugInfoTable(section)).ToList());
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
    public void GetInliners_DecodesSectionRelativeOffsetWithoutParsingTable()
    {
        byte[] content = Combine(
            Int32Bytes(2 * sizeof(int)),
            Int32Bytes(5),
            Int32Bytes(0),
            [0x32, 0x04]); // Nibbles: count 2, deltas 3 and 4.
        using ReadyToRunReader reader = CreateReader(24, 0, ReadyToRunSectionType.InliningInfo, content);
        ReadyToRunSection section = Assert.Single(reader.GetSections());

        IReadOnlyList<MethodRid> inliners = reader.GetInliners(section, (InlinerListOffset)12u);

        Assert.Equal([(MethodRid)3, (MethodRid)7], inliners);
        Assert.Equal((InlinerListOffset)12u, Assert.Single(reader.GetInliningInfoTable(section).Entries).InlinersOffset);
    }

    [Fact]
    public void GetInliners_OffsetOutsideSection_ThrowsBadImageFormat()
    {
        byte[] content = Combine(Int32Bytes(2 * sizeof(int)), Int32Bytes(5), Int32Bytes(0), [0x32, 0x04]);
        using ReadyToRunReader reader = CreateReader(24, 0, ReadyToRunSectionType.InliningInfo, content);
        ReadyToRunSection section = Assert.Single(reader.GetSections());

        Assert.Throws<BadImageFormatException>(() => reader.GetInliners(section, (InlinerListOffset)(uint)section.Size));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetPgoEntry_ResolvesInlineAndBackReferencedBlobs(bool backReference)
    {
        // Section-relative layout:
        //   0: hashtable header (1 bucket, 1-byte bucket index)
        //   1: bucket index [start=2, end=4], relative to offset 1
        //   3: bucket record [lowHash=0][signed relative offset to entry]
        //   5: back-referenced blob byte (or the entry when inline)
        // Back-reference entry at 6: [sig 0 2][versionAndFlags 7][delta 4] -> blob at 9 - 4 = 5.
        // Inline entry at 5: [sig 0 2][versionAndFlags 5][blob] -> blob at 8.
        byte[] content = backReference
            ? [0x00, 2, 4, 0, 4, 0xAA, 0, 2, 14, .. EncodeUnsigned(4)]
            : [0x00, 2, 4, 0, 2, 0, 2, 10, 0xAA];
        int expectedEntry = backReference ? 6 : 5;
        int expectedBlob = backReference ? 5 : 8;

        using ReadyToRunReader reader = CreateReader(24, 0, ReadyToRunSectionType.PgoInstrumentationData, content);
        ReadyToRunSection section = Assert.Single(reader.GetSections());
        int sectionOffset = (int)reader.GetOffsetForRVA(section.RelativeVirtualAddress);
        PgoHashTableHandle table = reader.GetPgoInstrumentationDataTable(section);

        PgoEntryHandle handle = Assert.Single(reader.EnumeratePgoEntries(table));
        Assert.Equal((PgoEntryHandle)expectedEntry, handle);
        Assert.Equal(handle, reader.LookupPgoEntry(table, 0, _ => true));

        PgoEntry entry = reader.GetPgoEntry(table, handle);

        Assert.Equal(1, entry.PgoFormatVersion);
        Assert.Equal(backReference ? (PgoDataBlobBackReferenceDelta)4u : null, entry.PgoDataBlobBackReferenceDelta);
        PgoDataBlobImageOffset blobOffset = reader.GetPgoDataBlobImageOffset(table, entry);
        Assert.Equal((PgoDataBlobImageOffset)(sectionOffset + expectedBlob), blobOffset);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(130)]
    public void GetFixupCells_ResolvesBackReferenceFromStartOfDelta(uint delta)
    {
        byte[] encodedDelta = EncodeUnsigned(delta);
        byte[] content = new byte[checked((int)delta + encodedDelta.Length)];
        content[0] = 0x21;
        int encodedIdOffset = checked((int)delta - 1);
        content[encodedIdOffset] = 6;
        encodedDelta.CopyTo(content, encodedIdOffset + 1);

        using ReadyToRunReader reader = CreateReader(24, 0, ReadyToRunSectionType.MethodDefEntryPoints, content);
        ReadyToRunSection section = Assert.Single(reader.GetSections());
        int sectionOffset = (int)reader.GetOffsetForRVA(section.RelativeVirtualAddress);
        (RuntimeFunctionIndex _, FixupCellListHandle? fixupCellList) =
            reader.DecodeRuntimeFunctionIdAndFixupCellList(
                sectionOffset + encodedIdOffset,
                sectionOffset,
                sectionOffset + section.Size);

        IReadOnlyList<FixupCellRef> fixupCells = reader.GetFixupCells(section, fixupCellList!.Value);

        FixupCellRef fixupCell = Assert.Single(fixupCells);
        Assert.Equal((ImportSectionIndex)1, fixupCell.TableIndex);
        Assert.Equal((ImportCellIndex)2, fixupCell.CellIndex);
    }

    [Fact]
    public void DecodeRuntimeFunctionIdAndFixupCellList_RejectsBackReferenceBeforeContainingRange()
    {
        byte[] content = [0, 0, 6, 8];
        using ReadyToRunReader reader = CreateReader(24, 0, ReadyToRunSectionType.MethodDefEntryPoints, content);
        ReadyToRunSection section = Assert.Single(reader.GetSections());
        int sectionOffset = (int)reader.GetOffsetForRVA(section.RelativeVirtualAddress);

        Assert.Throws<BadImageFormatException>(() =>
            reader.DecodeRuntimeFunctionIdAndFixupCellList(
                sectionOffset + 2,
                sectionOffset,
                sectionOffset + section.Size));
    }

    [Fact]
    public void GetPgoEntry_RejectsBackReferenceBeforeSectionStart()
    {
        // Entry at 5: [sig 0 2][versionAndFlags 7][delta 9]; the delta origin is 8, so 8 - 9 < 0.
        byte[] content = [0x00, 2, 4, 0, 2, 0, 2, 14, .. EncodeUnsigned(9)];
        using ReadyToRunReader reader = CreateReader(24, 0, ReadyToRunSectionType.PgoInstrumentationData, content);
        PgoHashTableHandle table = reader.GetPgoInstrumentationDataTable(Assert.Single(reader.GetSections()));

        Assert.Throws<BadImageFormatException>(() => reader.GetPgoEntry(table, (PgoEntryHandle)5));
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
        ReadyToRunSection section = Assert.Single(reader.GetSections());
        InliningInfoEntry entry = Assert.Single(reader.GetInliningInfoTable(section).Entries);

        Assert.Throws<BadImageFormatException>(() => reader.GetInliners(section, entry.InlinersOffset));
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
        switch (section)
        {
            case KnownReadyToRunSection { Type: ReadyToRunSectionType.RuntimeFunctions }:
                reader.GetRuntimeFunctionsTable(section);
                break;
            case KnownReadyToRunSection { Type: ReadyToRunSectionType.HotColdMap }:
                reader.GetHotColdMapTable(section);
                break;
            case KnownReadyToRunSection { Type: ReadyToRunSectionType.ComponentAssemblies }:
                reader.GetComponentAssembliesTable(section);
                break;
            case KnownReadyToRunSection { Type: ReadyToRunSectionType.ExceptionInfo }:
                reader.GetExceptionInfoTable(section);
                break;
            case KnownReadyToRunSection { Type: ReadyToRunSectionType.ImportSections }:
                reader.GetImportSectionsTableSection(section);
                break;
            case KnownReadyToRunSection { Type: ReadyToRunSectionType.EnclosingTypeMap }:
                reader.GetEnclosingTypeMapTable(section);
                break;
            case KnownReadyToRunSection { Type: ReadyToRunSectionType.MethodIsGenericMap }:
                reader.GetMethodIsGenericMapTable(section);
                break;
            case KnownReadyToRunSection { Type: ReadyToRunSectionType.TypeGenericInfoMap }:
                reader.GetTypeGenericInfoMapTable(section);
                break;
            default:
                throw new InvalidOperationException($"No parser registered for {section.RawType}.");
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

    private static byte[] EncodeUnsigned(uint value)
    {
        if (value <= 0x7F)
            return [(byte)(value << 1)];
        if (value <= 0x3FFF)
            return [(byte)((value << 2) | 1), (byte)(value >> 6)];
        if (value <= 0x1F_FFFF)
            return [(byte)((value << 3) | 3), (byte)(value >> 5), (byte)(value >> 13)];
        if (value <= 0x0FFF_FFFF)
            return [(byte)((value << 4) | 7), (byte)(value >> 4), (byte)(value >> 12), (byte)(value >> 20)];

        byte[] encoded = new byte[5];
        encoded[0] = 0x0F;
        BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(1), value);
        return encoded;
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
