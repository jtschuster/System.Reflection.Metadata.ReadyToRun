// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.ReadyToRun.Webcil;
using System.Text;

using Internal.Runtime;

using Xunit;

namespace System.Reflection.Metadata.ReadyToRun.Tests;

/// <summary>
/// Byte-level tests for structural readers of ReadyToRun section IDs 106, 111-113, 118, 124-126.
/// All tests use hand-constructed synthetic Webcil images to ensure deterministic coverage.
/// </summary>
public sealed class SectionCoverage100_126Tests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static ReadyToRunReader CreateReader(byte[] image)
        => new ReadyToRunReader(new WebcilImageReader(image), new NativeReader(new MemoryStream(image)));

    private static ReadyToRunSection FindSection(IReadOnlyList<ReadyToRunSection> sections, ReadyToRunSectionType type)
        => sections.Single(s => s.Type == type);

    /// <summary>
    /// Returns the content of a single-section Webcil image built from <paramref name="content"/>.
    /// File offset of the content within the image is returned via <paramref name="fileOffset"/>.
    /// </summary>
    private static byte[] BuildSingleSectionImage(
        ReadyToRunSectionType sectionType,
        byte[] content,
        out int fileOffset)
    {
        // Single R2R section → r2rHeaderSize = 16 + 1*12 = 28
        // firstSectionFileStart = V1HeaderSize(32) + coffSections(1)*SectionHeaderSize(16) + CorHeaderSize(72) = 120
        // fileOffset = 120 + 28 = 148
        fileOffset = 148;
        var specs = new[]
        {
            new WebcilImageBuilder.R2RSectionSpec(sectionType, content),
        };
        return WebcilImageBuilder.BuildRawWebcil(24, 0, specs);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Section 106 — DelayLoadMethodCallThunks
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DelayLoadMethodCallThunks_EmptySection_ZeroLength()
    {
        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.DelayLoadMethodCallThunks, Array.Empty<byte>(), out _);

        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.DelayLoadMethodCallThunks);
        DelayLoadMethodCallThunksSection thunks = reader.GetDelayLoadMethodCallThunksSection(section);

        Assert.Equal(0, thunks.Length);
        Assert.Equal(section.RelativeVirtualAddress, thunks.SectionRva);
    }

    [Fact]
    public void DelayLoadMethodCallThunks_OpaqueBytes_CorrectOffsetAndLength()
    {
        byte[] content = { 0x90, 0xFF, 0x25, 0xAB, 0xCD, 0xEF, 0x01, 0x23 }; // arbitrary opcodes
        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.DelayLoadMethodCallThunks, content, out _);

        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.DelayLoadMethodCallThunks);
        DelayLoadMethodCallThunksSection thunks = reader.GetDelayLoadMethodCallThunksSection(section);

        Assert.Equal(content.Length, thunks.Length);
        Assert.Equal(section.RelativeVirtualAddress, thunks.SectionRva);
        Assert.Equal(WasmMachine.Wasm32, reader.Machine);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Section 111 — ProfileDataInfo
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ProfileDataInfo_EmptySection_ReturnsEmptyList()
    {
        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.ProfileDataInfo, Array.Empty<byte>(), out _);

        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.ProfileDataInfo);
        ProfileDataInfoTable table = reader.GetProfileDataInfoTable(section);

        Assert.Empty(table.Entries);
    }

    [Fact]
    public void ProfileDataInfo_SingleRecordNullNext_ReturnsOneEntry()
    {
        // On Webcil, TargetPointerSize = 4 (Wasm32).
        // Record: [nextHandle: 4 bytes = 0][size: 4][detail: 4][methodToken: 4][ilSize: 4][blockCount: 4]
        byte[] content = new byte[]
        {
            0x00, 0x00, 0x00, 0x00,  // nextHandle = 0 (end of list)
            0x14, 0x00, 0x00, 0x00,  // size = 20 (five uint32 fields; excludes nextHandle)
            0x00, 0x00, 0x00, 0x00,  // detail = 0
            0x01, 0x00, 0x00, 0x06,  // methodToken = 0x06000001
            0x0A, 0x00, 0x00, 0x00,  // ilSize = 10
            0x00, 0x00, 0x00, 0x00,  // blockCount = 0
        };

        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.ProfileDataInfo, content, out _);

        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.ProfileDataInfo);
        ProfileDataInfoTable table = reader.GetProfileDataInfoTable(section);

        Assert.Single(table.Entries);
        ProfileDataInfoEntry entry = table.Entries[0];
        Assert.Equal(0UL, entry.NextHandle);
        Assert.Equal(20u, entry.Size);
        Assert.Equal(0u, entry.Detail);
        Assert.Equal(0x06000001u, entry.MethodToken);
        Assert.Equal(10u, entry.ILSize);
        Assert.Equal(0u, entry.BlockCount);
        Assert.Empty(entry.PayloadBytes);
    }

    [Fact]
    public void ProfileDataInfo_SizeExcludesNextPointerAndPreservesPayload()
    {
        byte[] payload = { 0x04, 0x00, 0x00, 0x00, 0x2A, 0x00, 0x00, 0x00 };
        byte[] content =
        [
            0x00, 0x00, 0x00, 0x00,  // nextHandle = 0
            0x1C, 0x00, 0x00, 0x00,  // size = 20-byte header + 8-byte payload
            0x00, 0x00, 0x00, 0x00,  // detail
            0x01, 0x00, 0x00, 0x06,  // methodToken
            0x0A, 0x00, 0x00, 0x00,  // ilSize
            0x01, 0x00, 0x00, 0x00,  // blockCount
            0x04, 0x00, 0x00, 0x00,  // block IL offset
            0x2A, 0x00, 0x00, 0x00,  // execution count
        ];

        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.ProfileDataInfo, content, out _);
        using ReadyToRunReader reader = CreateReader(image);
        ProfileDataInfoEntry entry = Assert.Single(reader.GetProfileDataInfoTable(
            FindSection(reader.GetSections(), ReadyToRunSectionType.ProfileDataInfo)).Entries);

        Assert.Equal(28u, entry.Size);
        Assert.Equal(payload, entry.PayloadBytes);
    }

    [Fact]
    public void ProfileDataInfo_RecordSizeSmallerThanHeader_ThrowsBadImageFormat()
    {
        byte[] content =
        [
            0x00, 0x00, 0x00, 0x00,
            0x13, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x00, 0x06,
            0x0A, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];
        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.ProfileDataInfo, content, out _);
        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.ProfileDataInfo);

        Assert.Throws<BadImageFormatException>(() => reader.GetProfileDataInfoTable(section));
    }

    [Fact]
    public void ProfileDataInfo_PayloadCannotExtendBeyondSection()
    {
        byte[] content =
        [
            0x00, 0x00, 0x00, 0x00,
            0x1C, 0x00, 0x00, 0x00, // Claims eight payload bytes.
            0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x00, 0x06,
            0x0A, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x00, 0x00,
            0x04, 0x00, 0x00, 0x00, // Only four payload bytes are present.
        ];
        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.ProfileDataInfo, content, out _);
        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.ProfileDataInfo);

        Assert.Throws<BadImageFormatException>(() => reader.GetProfileDataInfoTable(section));
    }

    [Fact]
    public void ProfileDataInfo_NonNullNextOnWebcil_ThrowsBadImageFormat()
    {
        // If nextHandle != 0, the reader calls TryGetFileOffsetFromImageVA which returns false on Webcil.
        byte[] content = new byte[]
        {
            0x01, 0x00, 0x20, 0x00,  // nextHandle = 0x00200001 (non-zero, invalid for Webcil)
            0x14, 0x00, 0x00, 0x00,  // size = 20
            0x00, 0x00, 0x00, 0x00,  // detail
            0x01, 0x00, 0x00, 0x06,  // methodToken
            0x0A, 0x00, 0x00, 0x00,  // ilSize
            0x00, 0x00, 0x00, 0x00,  // blockCount
        };

        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.ProfileDataInfo, content, out _);

        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.ProfileDataInfo);

        Assert.Throws<BadImageFormatException>(() => reader.GetProfileDataInfoTable(section));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Section 112 — ManifestMetadata
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ManifestMetadata_PassesThroughRvaAndSize()
    {
        byte[] content = new byte[32]; // arbitrary content
        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.ManifestMetadata, content, out _);

        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.ManifestMetadata);
        ManifestMetadataSection meta = reader.GetManifestMetadataSection(section);

        Assert.Equal(section.RelativeVirtualAddress, meta.SectionRva);
        Assert.Equal(content.Length, meta.SectionSize);
    }

    [Fact]
    public void ManifestMetadata_ZeroSize_ReturnsSectionDescriptor()
    {
        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.ManifestMetadata, Array.Empty<byte>(), out _);

        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.ManifestMetadata);
        ManifestMetadataSection meta = reader.GetManifestMetadataSection(section);

        Assert.Equal(section.RelativeVirtualAddress, meta.SectionRva);
        Assert.Equal(0, meta.SectionSize);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Section 113 — AttributePresence (NativeCuckooFilter)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AttributePresence_EmptySection_BucketCountZero_MayContainAlwaysFalse()
    {
        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.AttributePresence, Array.Empty<byte>(), out _);

        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.AttributePresence);
        AttributePresenceSection filter = reader.GetAttributePresenceSection(section);

        Assert.Equal(0, filter.BucketCount);
        Assert.False(filter.MayContain(0x0000_0000, 0x1234));
        Assert.False(filter.MayContain(0xFFFF_FFFF, 0xFFFF));
        Assert.Empty(filter.GetBuckets());
    }

    [Fact]
    public void AttributePresence_MisalignedNonZeroSection_ThrowsBadImageFormat()
    {
        // Single-section Webcil places content at file offset 148.
        // 148 % 16 = 4, so the filter is not 16-byte aligned → must throw.
        byte[] content = new byte[16]; // 16-byte section (but at a misaligned file offset)
        content[0] = 0xAB;             // some non-zero fingerprint to exercise the path

        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.AttributePresence, content, out int fileOffset);

        Assert.NotEqual(0, fileOffset % 16); // pre-condition: verify the offset is indeed not aligned

        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.AttributePresence);

        Assert.Throws<BadImageFormatException>(() => reader.GetAttributePresenceSection(section));
    }

    [Fact]
    public void AttributePresence_NonPowerOfTwoSize_ThrowsBadImageFormat()
    {
        byte[] filterData = new byte[48];
        using var stream = new MemoryStream(filterData);
        var nativeReader = new NativeReader(stream);

        Assert.Throws<BadImageFormatException>(() => new NativeCuckooFilter(nativeReader, 0, 48));
    }

    [Fact]
    public void NativeCuckooFilter_MayContain_DetectsStoredFingerprint()
    {
        // Build a 2-bucket (32-byte) filter with fingerprint 0x1234 in bucket 0, slot 0.
        byte[] filterData = new byte[32];
        filterData[0] = 0x34; // low byte
        filterData[1] = 0x12; // high byte → fingerprint[0] in bucket 0 = 0x1234

        using var stream = new MemoryStream(filterData);
        var nativeReader = new NativeReader(stream);
        var filter = new NativeCuckooFilter(nativeReader, 0, 32);

        // bucketCount = 32 / 16 = 2, bucketMask = 1
        // For hashcode where (hashcode & bucketMask) = 0 and fingerprint = 0x1234:
        //   bucketAIndex = 0 & 1 = 0  ← bucket A = bucket 0
        //   bucketBIndex = 0 ^ (0x1234 & 1) = 0 ^ 0 = 0  ← bucket B = bucket 0
        // Both A and B check bucket 0 → fingerprint 0x1234 is in slot 0 → true
        Assert.True(filter.MayContain(0x0000_0000, 0x1234));

        // A fingerprint not stored → false
        Assert.False(filter.MayContain(0x0000_0000, 0x5678));
    }

    [Fact]
    public void NativeCuckooFilter_MayContain_FingerprintZeroNormalized()
    {
        // fingerprint == 0 is normalized to 1. If 0x0001 is stored in bucket 0, MayContain(_, 0) returns true.
        byte[] filterData = new byte[16]; // 1 bucket
        filterData[0] = 0x01; // slot 0: fingerprint = 0x0001
        filterData[1] = 0x00;

        using var stream = new MemoryStream(filterData);
        var nativeReader = new NativeReader(stream);
        var filter = new NativeCuckooFilter(nativeReader, 0, 16);

        // fingerprint 0 → normalized to 1; 0x0001 is stored → should return true
        Assert.True(filter.MayContain(0x0000_0000, 0));
    }

    [Fact]
    public void NativeCuckooFilter_GetBuckets_ReturnsEightSlotsPerBucket()
    {
        byte[] filterData = new byte[32]; // 2 buckets
        using var stream = new MemoryStream(filterData);
        var nativeReader = new NativeReader(stream);
        var filter = new NativeCuckooFilter(nativeReader, 0, 32);

        ushort[][] buckets = filter.GetBuckets().ToArray();

        Assert.Equal(2, buckets.Length);
        foreach (ushort[] bucket in buckets)
            Assert.Equal(8, bucket.Length);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Section 118 — ManifestAssemblyMvids
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ManifestAssemblyMvids_EmptySection_ReturnsEmptyList()
    {
        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.ManifestAssemblyMvids, Array.Empty<byte>(), out _);

        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.ManifestAssemblyMvids);
        ManifestAssemblyMvidsTable table = reader.GetManifestAssemblyMvidsTable(section);

        Assert.Empty(table.Mvids);
    }

    [Fact]
    public void ManifestAssemblyMvids_TwoGuids_ParsedCorrectly()
    {
        var guid1 = new Guid("12345678-1234-1234-1234-123456789012");
        var guid2 = new Guid("ABCDEFAB-CDEF-ABCD-EFAB-CDEFABCDEFAB");

        byte[] content = new byte[32];
        guid1.ToByteArray().CopyTo(content, 0);
        guid2.ToByteArray().CopyTo(content, 16);

        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.ManifestAssemblyMvids, content, out _);

        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.ManifestAssemblyMvids);
        ManifestAssemblyMvidsTable table = reader.GetManifestAssemblyMvidsTable(section);

        Assert.Equal(2, table.Mvids.Count);
        Assert.Equal(guid1, table.Mvids[0]);
        Assert.Equal(guid2, table.Mvids[1]);
    }

    [Fact]
    public void ManifestAssemblyMvids_TruncatedSection_ThrowsBadImageFormat()
    {
        // 17 bytes is not a multiple of 16 → truncated GUID section.
        byte[] content = new byte[17];
        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.ManifestAssemblyMvids, content, out _);

        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.ManifestAssemblyMvids);

        Assert.Throws<BadImageFormatException>(() => reader.GetManifestAssemblyMvidsTable(section));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Section 124 — ExternalTypeMaps
    // ──────────────────────────────────────────────────────────────────────────

    // Empty one-bucket NativeHashtable: both bucket offsets point just past the two-byte index.
    private static readonly byte[] EmptyNativeHashtable = { 0x00, 0x02, 0x02 };

    /// <summary>
    /// Builds a single-bucket NativeHashtable containing exactly one entry.
    /// </summary>
    /// <param name="entryData">The data bytes of the entry (what the caller reads via curParser.GetUnsigned etc.).</param>
    /// <param name="lowHashcode">The low hashcode byte stored with the entry.</param>
    /// <remarks>
    /// NativeHashtable format: the bucket index range covers ONLY [lhc][delta] (2 bytes per entry).
    /// Actual entry data is placed OUTSIDE the bucket range; the delta (relative offset) points to it.
    /// With delta=1 (encoded 0x02), data is immediately after the delta byte, at bucket_end relative to _baseOffset.
    /// AllEntriesEnumerator stops when _parser.Offset reaches endOffset — which equals the start of the data.
    /// </remarks>
    private static byte[] BuildOneEntryHashtable(byte[] entryData, byte lowHashcode = 0x00)
    {
        // Layout: [header=0x00][start=2][end=4][lhc][delta=0x02][entryData...]
        //          offset 0      1       2      3    4             5...
        // _baseOffset = fileOffset+1; bucket range [2,4) = 2 bytes = [lhc][delta].
        // delta=1 (0x02): data starts at _baseOffset+4 = right after bucket end.
        // After reading delta, _parser.Offset = _baseOffset+4 = endOffset → loop exits.
        var table = new byte[5 + entryData.Length];
        table[0] = 0x00; // header: 1 bucket (1<<0=1), entryIndexSize=0
        table[1] = 2;    // bucket 0 start index (relative to _baseOffset)
        table[2] = 4;    // bucket 0 end index — covers ONLY [lhc][delta], NOT entryData
        table[3] = lowHashcode;
        table[4] = 0x02; // DecodeSigned(0x02) = 1 → data at pos_before_delta + 1
        entryData.CopyTo(table, 5);
        return table;
    }

    private static byte[] EncodeUnsigned(uint value)
    {
        if (value < 0x40)
            return new[] { (byte)(value << 1) };
        if (value < 0x4000)
            return new[] { (byte)((value << 2) | 1), (byte)(value >> 6) };
        if (value < 0x20_0000)
            return new[] { (byte)((value << 3) | 3), (byte)(value >> 5), (byte)(value >> 13) };
        if (value < 0x1000_0000)
        {
            return new[]
            {
                (byte)((value << 4) | 7),
                (byte)(value >> 4),
                (byte)(value >> 12),
                (byte)(value >> 20),
            };
        }

        return new[]
        {
            (byte)0x0F,
            (byte)value,
            (byte)(value >> 8),
            (byte)(value >> 16),
            (byte)(value >> 24),
        };
    }

    private static byte[] ImportRef(uint sectionIdx, uint fixupIdx)
        => EncodeUnsigned(sectionIdx).Concat(EncodeUnsigned(fixupIdx)).ToArray();

    [Fact]
    public void ExternalTypeMaps_EmptyOuterHashtable_NoGroups()
    {
        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.ExternalTypeMaps, EmptyNativeHashtable, out _);

        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.ExternalTypeMaps);
        ExternalTypeMapsTable table = reader.GetExternalTypeMapsTable(section);

        Assert.Empty(table.Groups);
    }

    [Fact]
    public void ExternalTypeMaps_InvalidState_GroupHasDefaultInnerHandle()
    {
        // Outer entry: groupRef=(1,2), state=0 (invalid)
        byte[] entryData = ImportRef(1, 2).Concat(EncodeUnsigned(0)).ToArray(); // [groupRef][state=0]
        byte[] content = BuildOneEntryHashtable(entryData);

        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.ExternalTypeMaps, content, out _);

        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.ExternalTypeMaps);
        ExternalTypeMapsTable table = reader.GetExternalTypeMapsTable(section);

        Assert.Single(table.Groups);
        ExternalTypeMapGroup g = table.Groups[0];
        Assert.Equal(new ImportFixupReference(1, 2), g.GroupRef);
        Assert.Equal(0u, g.State);
        Assert.Equal(default, g.InnerHandle); // no inner table when invalid
    }

    [Fact]
    public void ExternalTypeMaps_ValidGroupWithEmptyInner_NoInnerEntries()
    {
        // Outer entry: groupRef=(1,2), state=1, inner=empty hashtable
        byte[] groupData = ImportRef(1, 2).Concat(EncodeUnsigned(1)).ToArray();
        byte[] entryData = groupData.Concat(EmptyNativeHashtable).ToArray();
        byte[] content = BuildOneEntryHashtable(entryData);

        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.ExternalTypeMaps, content, out _);

        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.ExternalTypeMaps);
        ExternalTypeMapsTable outerTable = reader.GetExternalTypeMapsTable(section);

        Assert.Single(outerTable.Groups);
        ExternalTypeMapGroup g = outerTable.Groups[0];
        Assert.Equal(1u, g.State);
        Assert.NotEqual(default, g.InnerHandle);

        IReadOnlyList<ExternalTypeMapEntry> innerEntries = reader.GetExternalTypeMapEntries(g.InnerHandle);
        Assert.Empty(innerEntries);
    }

    [Fact]
    public void ExternalTypeMaps_ValidGroupWithStringEntry_ParsedCorrectly()
    {
        // Inner entry: key="Foo", result=(3,4)
        string key = "Foo";
        byte[] keyBytes = Encoding.UTF8.GetBytes(key);
        byte[] innerEntryData = EncodeUnsigned((uint)keyBytes.Length)
            .Concat(keyBytes)
            .Concat(ImportRef(3, 4))
            .ToArray();
        byte[] innerTableContent = BuildOneEntryHashtable(innerEntryData);

        // Outer entry: groupRef=(1,2), state=1, inner=the above table
        byte[] outerEntryData = ImportRef(1, 2)
            .Concat(EncodeUnsigned(1))
            .Concat(innerTableContent)
            .ToArray();
        byte[] content = BuildOneEntryHashtable(outerEntryData);

        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.ExternalTypeMaps, content, out _);

        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.ExternalTypeMaps);
        ExternalTypeMapsTable outerTable = reader.GetExternalTypeMapsTable(section);

        Assert.Single(outerTable.Groups);
        ExternalTypeMapGroup g = outerTable.Groups[0];
        Assert.Equal(new ImportFixupReference(1, 2), g.GroupRef);
        Assert.Equal(1u, g.State);

        IReadOnlyList<ExternalTypeMapEntry> innerEntries = reader.GetExternalTypeMapEntries(g.InnerHandle);

        Assert.Single(innerEntries);
        ExternalTypeMapEntry e = innerEntries[0];
        Assert.Equal("Foo", e.Key);
        Assert.Equal(new ImportFixupReference(3, 4), e.ResultRef);
        Assert.Throws<ArgumentException>(() => reader.GetExternalTypeMapEntries(
            (ExternalTypeMapInnerHandle)((uint)g.InnerHandle + 1)));
    }

    [Fact]
    public void ExternalTypeMaps_KeyLengthCannotExtendBeyondSection()
    {
        byte[] innerTableContent = BuildOneEntryHashtable(EncodeUnsigned(63));
        byte[] outerEntryData = ImportRef(1, 2)
            .Concat(EncodeUnsigned(1))
            .Concat(innerTableContent)
            .ToArray();
        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.ExternalTypeMaps,
            BuildOneEntryHashtable(outerEntryData),
            out _);
        using ReadyToRunReader reader = CreateReader(image);
        ExternalTypeMapGroup group = Assert.Single(reader.GetExternalTypeMapsTable(
            FindSection(reader.GetSections(), ReadyToRunSectionType.ExternalTypeMaps)).Groups);

        Assert.Throws<BadImageFormatException>(() => reader.GetExternalTypeMapEntries(group.InnerHandle));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Section 125 — ProxyTypeMaps
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ProxyTypeMaps_EmptyOuterHashtable_NoGroups()
    {
        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.ProxyTypeMaps, EmptyNativeHashtable, out _);

        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.ProxyTypeMaps);
        ProxyTypeMapsTable table = reader.GetProxyTypeMapsTable(section);

        Assert.Empty(table.Groups);
    }

    [Fact]
    public void ProxyTypeMaps_ValidGroupWithInnerEntry_ParsedCorrectly()
    {
        // Inner entry: key=(1,2), value=(3,4)
        byte[] innerEntryData = ImportRef(1, 2).Concat(ImportRef(3, 4)).ToArray();
        byte[] innerTableContent = BuildOneEntryHashtable(innerEntryData);

        // Outer entry: groupRef=(5,6), state=1
        byte[] outerEntryData = ImportRef(5, 6)
            .Concat(EncodeUnsigned(1))
            .Concat(innerTableContent)
            .ToArray();
        byte[] content = BuildOneEntryHashtable(outerEntryData);

        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.ProxyTypeMaps, content, out _);

        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.ProxyTypeMaps);
        ProxyTypeMapsTable outerTable = reader.GetProxyTypeMapsTable(section);

        Assert.Single(outerTable.Groups);
        ProxyTypeMapGroup g = outerTable.Groups[0];
        Assert.Equal(new ImportFixupReference(5, 6), g.GroupRef);
        Assert.Equal(1u, g.State);

        IReadOnlyList<ProxyTypeMapEntry> innerEntries = reader.GetProxyTypeMapEntries(g.InnerHandle);

        Assert.Single(innerEntries);
        ProxyTypeMapEntry e = innerEntries[0];
        Assert.Equal(new ImportFixupReference(1, 2), e.KeyRef);
        Assert.Equal(new ImportFixupReference(3, 4), e.ValueRef);
        Assert.Throws<ArgumentException>(() => reader.GetProxyTypeMapEntries(
            (ProxyTypeMapInnerHandle)((uint)g.InnerHandle + 1)));
    }

    [Fact]
    public void ProxyTypeMaps_InvalidState_EmptyInnerHandleReturnsNoEntries()
    {
        byte[] entryData = ImportRef(1, 2).Concat(EncodeUnsigned(0)).ToArray(); // state=0
        byte[] content = BuildOneEntryHashtable(entryData);

        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.ProxyTypeMaps, content, out _);

        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.ProxyTypeMaps);
        ProxyTypeMapsTable table = reader.GetProxyTypeMapsTable(section);

        Assert.Single(table.Groups);
        ProxyTypeMapGroup g = table.Groups[0];
        Assert.Equal(0u, g.State);
        Assert.Equal(default, g.InnerHandle);

        IReadOnlyList<ProxyTypeMapEntry> entries = reader.GetProxyTypeMapEntries(g.InnerHandle);
        Assert.Empty(entries);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Section 126 — TypeMapAssemblyTargets
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TypeMapAssemblyTargets_EmptyHashtable_NoEntries()
    {
        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.TypeMapAssemblyTargets, EmptyNativeHashtable, out _);

        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.TypeMapAssemblyTargets);
        TypeMapAssemblyTargetsTable table = reader.GetTypeMapAssemblyTargetsTable(section);

        Assert.Empty(table.Entries);
    }

    [Fact]
    public void TypeMapAssemblyTargets_EmptyModuleList_ParsedCorrectly()
    {
        // Entry: groupRef=(1,2), moduleCount=0 → no modules
        byte[] entryData = ImportRef(1, 2).Concat(EncodeUnsigned(0)).ToArray();
        byte[] content = BuildOneEntryHashtable(entryData);

        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.TypeMapAssemblyTargets, content, out _);

        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.TypeMapAssemblyTargets);
        TypeMapAssemblyTargetsTable table = reader.GetTypeMapAssemblyTargetsTable(section);

        Assert.Single(table.Entries);
        TypeMapAssemblyTargetsEntry e = table.Entries[0];
        Assert.Equal(new ImportFixupReference(1, 2), e.GroupRef);
        Assert.Empty(e.ModuleRefs);
    }

    [Fact]
    public void TypeMapAssemblyTargets_ThreeModules_ParsedCorrectly()
    {
        // Entry: groupRef=(1,2), moduleCount=3, modules=[(3,4),(5,6),(7,8)]
        byte[] entryData = ImportRef(1, 2)
            .Concat(EncodeUnsigned(3))        // moduleCount
            .Concat(ImportRef(3, 4))           // module 0
            .Concat(ImportRef(5, 6))           // module 1
            .Concat(ImportRef(7, 8))           // module 2
            .ToArray();
        byte[] content = BuildOneEntryHashtable(entryData);

        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.TypeMapAssemblyTargets, content, out _);

        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.TypeMapAssemblyTargets);
        TypeMapAssemblyTargetsTable table = reader.GetTypeMapAssemblyTargetsTable(section);

        Assert.Single(table.Entries);
        TypeMapAssemblyTargetsEntry e = table.Entries[0];
        Assert.Equal(new ImportFixupReference(1, 2), e.GroupRef);
        Assert.Equal(3, e.ModuleRefs.Count);
        Assert.Equal(new ImportFixupReference(3, 4), e.ModuleRefs[0]);
        Assert.Equal(new ImportFixupReference(5, 6), e.ModuleRefs[1]);
        Assert.Equal(new ImportFixupReference(7, 8), e.ModuleRefs[2]);
    }

    [Fact]
    public void TypeMapAssemblyTargets_HugeModuleCount_ThrowsBeforeAllocation()
    {
        byte[] entryData = ImportRef(1, 2)
            .Concat(EncodeUnsigned(int.MaxValue))
            .ToArray();
        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.TypeMapAssemblyTargets,
            BuildOneEntryHashtable(entryData),
            out _);
        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = FindSection(reader.GetSections(), ReadyToRunSectionType.TypeMapAssemblyTargets);

        Assert.Throws<BadImageFormatException>(() => reader.GetTypeMapAssemblyTargetsTable(section));
    }

    [Fact]
    public void NewTypedSectionReaders_RejectUnknownFutureMajorVersion()
    {
        byte[] image = WebcilImageBuilder.BuildRawWebcil(
            25,
            0,
            [new WebcilImageBuilder.R2RSectionSpec(ReadyToRunSectionType.ManifestAssemblyMvids, Array.Empty<byte>())]);
        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = Assert.Single(reader.GetSections());

        Assert.Throws<NotSupportedException>(() => reader.GetManifestAssemblyMvidsTable(section));
        Assert.Empty(reader.GetSectionBytes(section));
    }

    [Fact]
    public void NewTypedSectionReaders_RejectMismatchedSectionType()
    {
        byte[] image = BuildSingleSectionImage(
            ReadyToRunSectionType.ManifestMetadata,
            Array.Empty<byte>(),
            out _);
        using ReadyToRunReader reader = CreateReader(image);
        ReadyToRunSection section = Assert.Single(reader.GetSections());

        Assert.Throws<ArgumentException>(() => reader.GetDelayLoadMethodCallThunksSection(section));
    }
}
