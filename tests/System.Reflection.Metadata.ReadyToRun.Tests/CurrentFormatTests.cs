// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection.Metadata.ReadyToRun.Webcil;
using System.Reflection.PortableExecutable;
using Internal.ReadyToRunConstants;
using Internal.Runtime;
using Xunit;

namespace System.Reflection.Metadata.ReadyToRun.Tests;

public sealed class CurrentFormatTests
{
    [CurrentFormatImageFact]
    public void CurrentCrossgen2ImageHasReadablePrefixedImportSignatures()
    {
        string path = Environment.GetEnvironmentVariable("R2R_CURRENT_IMAGE_PATH")!;
        using var pe = new PEReader(File.OpenRead(path));
        using var reader = new ReadyToRunReader(new PEImageReader(pe), new NativeReader(File.OpenRead(path), leaveOpen: false),
            path, new ReadyToRunReaderOptions(ReadyToRunValidationMode.Strict));
        Assert.Equal((ushort)34, reader.GetHeader().MajorVersion);
        ReadyToRunSection imports = Assert.Single(reader.GetSections(), section => section.Type == ReadyToRunSectionType.ImportSections);
        int decoded = 0;
        foreach (var entry in reader.GetImportSectionsTableSection(imports).Entries)
        {
            if ((entry.Flags & ReadyToRunImportSectionFlags.GCRefMap) == 0)
                continue;
            SignatureTable table = reader.GetSignatureTable(entry.SignatureTableRva, reader.GetImportSectionEntryCount(entry));
            foreach (SignatureRva signature in table.Entries)
            {
                Assert.NotNull(reader.GetImportSignature(entry, signature).Fixup);
                decoded++;
            }
        }
        Assert.True(decoded > 0, "The image did not contain any GC-map-prefixed import signatures.");
    }

    [Theory]
    [InlineData(124)]
    [InlineData(125)]
    public void TypeMapsReadNamedEntriesAfterTheHashtable(int sectionType)
    {
        byte[] content = [0, 2, 4, 0, 2, 0, 0, 4, 0, 2, 2, 2, 2, (byte)'a', 2, (byte)'b'];
        using ReadyToRunReader reader = CreateReader(28, 0, (ReadyToRunSectionType)sectionType, content);
        ReadyToRunSection section = Assert.Single(reader.GetSections());
        IReadOnlyList<TypeMapNamedEntry> entries;
        if (sectionType == 124)
        {
            var group = Assert.Single(reader.GetExternalTypeMapsTable(section).Groups);
            Assert.Empty(reader.GetExternalTypeMapEntries(group.InnerHandle));
            entries = reader.GetExternalTypeMapNamedEntries(group.InnerHandle);
        }

        else
        {
            var group = Assert.Single(reader.GetProxyTypeMapsTable(section).Groups);
            Assert.Empty(reader.GetProxyTypeMapEntries(group.InnerHandle));
            entries = reader.GetProxyTypeMapNamedEntries(group.InnerHandle);
        }
        TypeMapNamedEntry namedEntry = Assert.Single(entries);
        Assert.Equal("a", namedEntry.Key);
        Assert.Equal("b", namedEntry.Target);
    }

    [Fact]
    public void NamedTypeMapTruncationIsRejected()
    {
        byte[] content = [0, 2, 4, 0, 2, 0, 0, 4, 0, 2, 2, 2, 6, (byte)'a'];
        using ReadyToRunReader reader = CreateReader(28, 0, ReadyToRunSectionType.ExternalTypeMaps, content);
        var group = Assert.Single(reader.GetExternalTypeMapsTable(Assert.Single(reader.GetSections())).Groups);
        Assert.Throws<BadImageFormatException>(() => reader.GetExternalTypeMapNamedEntries(group.InnerHandle));
    }

    [Fact]
    public void ImportSignaturesDecodePrefixedMapsAndLegacyCookie()
    {
        byte[] content = [0x17, (byte)ReadyToRunFixupKind.Helper, (byte)ReadyToRunHelper.ThrowExact];
        using ReadyToRunReader reader = CreateReader(33, 0, ReadyToRunSectionType.CompilerIdentifier, content);
        var section = Assert.Single(reader.GetSections());
        var entry = new ImportSectionEntry(default, 0, ReadyToRunImportSectionFlags.GCRefMap, default, 0, default, default);
        var result = reader.GetImportSignature(entry, (SignatureRva)(uint)section.RelativeVirtualAddress);
        Assert.Equal(GCRefMapToken.ThisRef, Assert.Single(result.GCRefMap.Entries).Token);
        Assert.Equal((uint)section.RelativeVirtualAddress + 1, (uint)result.FixupSignatureRva);
        Assert.Equal(ReadyToRunHelper.ThrowExact, Assert.IsType<R2RHelperFixupPayload>(result.Fixup.Payload).HelperId);

        using ReadyToRunReader legacy = CreateReader(32, 0, ReadyToRunSectionType.CompilerIdentifier, [4, 0, 0, 0, 0x17]);
        var oldSection = Assert.Single(legacy.GetSections());
        var table = legacy.GetGCRefMapTable((AuxiliaryDataTableRva)(uint)oldSection.RelativeVirtualAddress, 1);
        Assert.Equal(GCRefMapToken.VaSigCookie, Assert.Single(Assert.Single(table.Entries).Entries).Token);
    }

    [Theory]
    [InlineData(new byte[] { 0x1F }, GCRefMapToken.ThisByRef)]
    [InlineData(new byte[] { 0xE7, 0x00 }, GCRefMapToken.VaSigCookie)]
    public void PrefixedImportMapsDecodeCurrentReceiverAndCookieTokens(byte[] map, GCRefMapToken token)
    {
        byte[] content = map.Concat(new byte[] { (byte)ReadyToRunFixupKind.Helper, (byte)ReadyToRunHelper.ThrowExact }).ToArray();
        using ReadyToRunReader reader = CreateReader(33, 0, ReadyToRunSectionType.CompilerIdentifier, content);
        var entry = new ImportSectionEntry(default, 0, ReadyToRunImportSectionFlags.GCRefMap, default, 0, default, default);
        var result = reader.GetImportSignature(entry, (SignatureRva)(uint)Assert.Single(reader.GetSections()).RelativeVirtualAddress);
        Assert.Equal(token, Assert.Single(result.GCRefMap.Entries).Token);
        Assert.Equal(ReadyToRunHelper.ThrowExact, Assert.IsType<R2RHelperFixupPayload>(result.Fixup.Payload).HelperId);
    }

    [Fact]
    public void WasmResumeInfoReadsChunksAndRejectsTruncation()
    {
        using ReadyToRunReader reader = CreateReader(34, 0, ReadyToRunSectionType.WasmAsyncResumeInfo, [2, 0x80, 1, 2, 8, 4, 1, 8]);
        var table = reader.GetWasmAsyncResumeInfoTable(Assert.Single(reader.GetSections()));
        Assert.Collection(table.Chunks,
            first => { Assert.Equal(128u, first.Delta); Assert.Equal(2u, first.Count); Assert.Equal(8u, first.Stride); },
            second => { Assert.Equal(4u, second.Delta); Assert.Equal(1u, second.Count); Assert.Equal(8u, second.Stride); });

        using ReadyToRunReader truncated = CreateReader(34, 0, ReadyToRunSectionType.WasmAsyncResumeInfo, [1, 0x80]);
        Assert.Throws<BadImageFormatException>(() => truncated.GetWasmAsyncResumeInfoTable(Assert.Single(truncated.GetSections())));
    }

    [Fact]
    public void WasmUnwindInfoDecodesFrameAndVirtualInstructionPointerSpan()
    {
        using ReadyToRunReader reader = CreateReader(34, 0, ReadyToRunSectionType.CompilerIdentifier, [0x80, 1, 3]);
        var section = Assert.Single(reader.GetSections());
        var unwind = Assert.IsType<WasmUnwindInfo>(reader.GetUnwindInfo((UnwindInfoRva)(uint)section.RelativeVirtualAddress));
        Assert.Equal(128u, unwind.FrameSize);
        Assert.Equal(6u, unwind.VirtualIPCount);
        Assert.Equal(3, unwind.Size);
    }

    [Theory]
    [InlineData(29, 1, 0x1000)]
    [InlineData(31, 0, 0x2000)]
    public void NewHeaderFlagsAreVersionGated(ushort major, ushort minor, uint flags)
    {
        byte[] image = WebcilImageBuilder.BuildRawWebcil(major, minor, [], r2rFlags: flags);
        using var reader = new ReadyToRunReader(new WebcilImageReader(image), new NativeReader(new MemoryStream(image)),
            options: new ReadyToRunReaderOptions(ReadyToRunValidationMode.Strict));
        Assert.Throws<NotSupportedException>(() => reader.GetHeader());
    }

    [Theory]
    [InlineData(29, 2, 0x1000)]
    [InlineData(31, 1, 0x2000)]
    public void NewHeaderFlagsAreAcceptedAtTheirIntroducedVersions(ushort major, ushort minor, uint flags)
    {
        byte[] image = WebcilImageBuilder.BuildRawWebcil(major, minor, [], r2rFlags: flags);
        using var reader = new ReadyToRunReader(new WebcilImageReader(image), new NativeReader(new MemoryStream(image)),
            options: new ReadyToRunReaderOptions(ReadyToRunValidationMode.Strict));
        Assert.Equal(flags, reader.GetHeader().Flags);
    }

    private static ReadyToRunReader CreateReader(ushort major, ushort minor, ReadyToRunSectionType type, byte[] content)
    {
        byte[] image = WebcilImageBuilder.BuildRawWebcil(major, minor, [new WebcilImageBuilder.R2RSectionSpec(type, content)]);
        return new ReadyToRunReader(new WebcilImageReader(image), new NativeReader(new MemoryStream(image)),
            options: new ReadyToRunReaderOptions(ReadyToRunValidationMode.Strict));
    }
}

internal sealed class CurrentFormatImageFactAttribute : FactAttribute
{
    public CurrentFormatImageFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("R2R_CURRENT_IMAGE_PATH")))
            Skip = "Set R2R_CURRENT_IMAGE_PATH to a ReadyToRun 34 image to test current Crossgen2 output.";
    }
}
