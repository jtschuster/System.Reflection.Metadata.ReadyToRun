// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers.Binary;
using System.Reflection.Metadata.ReadyToRun.Webcil;

using Internal.Runtime;

using Xunit;

namespace System.Reflection.Metadata.ReadyToRun.Tests;

public sealed class ReadyToRunCompatibilityTests
{
    public static TheoryData<ushort, ushort> StableVersions => new()
    {
        { 1, 2 },
        { 2, 0 },
        { 2, 2 },
        { 3, 1 },
        { 4, 1 },
        { 5, 4 },
        { 8, 0 },
        { 9, 1 },
        { 10, 1 },
        { 16, 0 },
        { 24, 0 },
    };

    public static TheoryData<ushort, ushort> TransitionVersions => new()
    {
        { 2, 1 },
        { 2, 3 },
        { 3, 0 },
        { 3, 2 },
        { 4, 2 },
        { 5, 1 },
        { 5, 2 },
        { 5, 3 },
        { 6, 0 },
        { 6, 1 },
        { 6, 2 },
        { 6, 3 },
        { 7, 0 },
        { 7, 1 },
        { 9, 0 },
        { 9, 2 },
        { 9, 3 },
        { 10, 0 },
        { 11, 0 },
        { 12, 0 },
        { 13, 0 },
        { 13, 1 },
        { 14, 0 },
        { 15, 0 },
        { 17, 0 },
        { 17, 1 },
        { 18, 0 },
        { 18, 1 },
        { 18, 2 },
        { 18, 3 },
        { 18, 4 },
        { 18, 5 },
        { 18, 6 },
        { 18, 7 },
        { 19, 0 },
        { 20, 0 },
        { 21, 0 },
        { 22, 0 },
        { 23, 0 },
    };

    [Theory]
    [MemberData(nameof(StableVersions))]
    [MemberData(nameof(TransitionVersions))]
    public void KnownVersionsAreSupportedInStrictMode(ushort majorVersion, ushort minorVersion)
    {
        byte[] image = BuildImage(majorVersion, minorVersion);
        using ReadyToRunReader reader = CreateReader(image, ReadyToRunValidationMode.Strict);

        Assert.Equal(ReadyToRunFormatSupport.Supported, reader.FormatSupport);
        Assert.Equal(majorVersion, reader.ReadyToRunHeader.MajorVersion);
        Assert.Equal(minorVersion, reader.ReadyToRunHeader.MinorVersion);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 0)]
    [InlineData(18, 8)]
    [InlineData(24, 1)]
    public void UnknownMinorVersionUsesKnownLayoutsInTolerantMode(ushort majorVersion, ushort minorVersion)
    {
        byte[] content = { 0x41, 0x42 };
        byte[] image = BuildImage(majorVersion, minorVersion, ReadyToRunSectionType.CompilerIdentifier, content);
        using ReadyToRunReader reader = CreateReader(image);

        ReadyToRunHeader header = reader.ReadyToRunHeader;
        ReadyToRunSection section = Assert.Single(header.Sections);

        Assert.Equal(ReadyToRunFormatSupport.CompatibleUnknownMinorVersion, header.FormatSupport);
        Assert.Equal(ReadyToRunFormatSupport.CompatibleUnknownMinorVersion, reader.FormatSupport);
        Assert.Equal(content, reader.GetSectionBytes(section));
        Assert.Equal("AB", reader.GetCompilerIdentifier(section));
    }

    [Fact]
    public void UnknownMinorVersionIsRejectedInStrictMode()
    {
        byte[] image = BuildImage(24, 1);
        using ReadyToRunReader reader = CreateReader(image, ReadyToRunValidationMode.Strict);

        Assert.Throws<NotSupportedException>(() => reader.ReadyToRunHeader);
    }

    [Fact]
    public void UnknownMajorVersionPreservesOnlyRawDataInTolerantMode()
    {
        byte[] content = { 0x41, 0x42 };
        byte[] image = BuildImage(25, 0, ReadyToRunSectionType.CompilerIdentifier, content);
        using ReadyToRunReader reader = CreateReader(image);

        ReadyToRunSection section = Assert.Single(reader.GetSections());
        Assert.Equal(ReadyToRunFormatSupport.UnknownMajorVersion, reader.FormatSupport);
        Assert.Equal(content, reader.GetSectionBytes(section));
        Assert.Throws<NotSupportedException>(() => reader.GetCompilerIdentifier(section));
    }

    [Fact]
    public void UnknownMajorVersionIsRejectedInStrictMode()
    {
        byte[] image = BuildImage(25, 0);
        using ReadyToRunReader reader = CreateReader(image, ReadyToRunValidationMode.Strict);

        Assert.Throws<NotSupportedException>(() => reader.ReadyToRunHeader);
    }

    [Fact]
    public void UnknownSectionTypeIsPreservedInTolerantMode()
    {
        const int unknownSectionType = 999;
        byte[] content = { 1, 2, 3, 4 };
        byte[] image = BuildImage(24, 0, (ReadyToRunSectionType)unknownSectionType, content);
        using ReadyToRunReader reader = CreateReader(image);

        ReadyToRunSection section = Assert.Single(reader.GetSections());
        Assert.Equal(unknownSectionType, (int)section.Type);
        Assert.Equal(content, reader.GetSectionBytes(section));
    }

    [Fact]
    public void UnknownSectionTypeIsRejectedInStrictMode()
    {
        byte[] image = BuildImage(24, 0, (ReadyToRunSectionType)999, new byte[] { 1 });
        using ReadyToRunReader reader = CreateReader(image, ReadyToRunValidationMode.Strict);

        Assert.Throws<NotSupportedException>(() => reader.GetSections());
    }

    [Fact]
    public void UnknownHeaderFlagsArePreservedInTolerantMode()
    {
        const uint unknownFlag = 0x80000000;
        byte[] image = BuildImage(24, 0, flags: unknownFlag);
        using ReadyToRunReader reader = CreateReader(image);

        Assert.Equal(unknownFlag, reader.ReadyToRunHeader.Flags);
    }

    [Fact]
    public void UnknownHeaderFlagsAreRejectedInStrictMode()
    {
        byte[] image = BuildImage(24, 0, flags: 0x80000000);
        using ReadyToRunReader reader = CreateReader(image, ReadyToRunValidationMode.Strict);

        Assert.Throws<NotSupportedException>(() => reader.ReadyToRunHeader);
    }

    [Fact]
    public void ComponentAssemblyIndexBaseChangesAtVersionSixPointThree()
    {
        using ReadyToRunReader versionSixPointTwo = CreateReader(BuildImage(6, 2));
        using ReadyToRunReader versionSixPointThree = CreateReader(BuildImage(6, 3));

        Assert.Equal(1, versionSixPointTwo.ComponentAssemblyIndexOffset);
        Assert.Equal(2, versionSixPointThree.ComponentAssemblyIndexOffset);
    }

    [Fact]
    public void OversizedSectionCountIsMalformed()
    {
        byte[] image = BuildImage(24, 0);
        int headerOffset = FindReadyToRunHeader(image);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(headerOffset + 12), uint.MaxValue);
        using ReadyToRunReader reader = CreateReader(image);

        Assert.Throws<BadImageFormatException>(() => reader.ReadyToRunHeader);
    }

    [Fact]
    public void DuplicateSectionTypesAreMalformed()
    {
        byte[] image = WebcilImageBuilder.BuildRawWebcil(
            24,
            0,
            new[]
            {
                new WebcilImageBuilder.R2RSectionSpec(ReadyToRunSectionType.CompilerIdentifier, new byte[] { 0 }),
                new WebcilImageBuilder.R2RSectionSpec(ReadyToRunSectionType.CompilerIdentifier, new byte[] { 0 }),
            });
        using ReadyToRunReader reader = CreateReader(image);

        Assert.Throws<BadImageFormatException>(() => reader.ReadyToRunHeader);
    }

    [Fact]
    public void UnmappedSectionRangeIsMalformed()
    {
        byte[] image = BuildImage(24, 0, ReadyToRunSectionType.CompilerIdentifier, new byte[] { 0 });
        int headerOffset = FindReadyToRunHeader(image);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(headerOffset + 20), 0x7FFFFFF0);
        using ReadyToRunReader reader = CreateReader(image);

        Assert.Throws<BadImageFormatException>(() => reader.ReadyToRunHeader);
    }

    private static byte[] BuildImage(
        ushort majorVersion,
        ushort minorVersion,
        ReadyToRunSectionType? sectionType = null,
        byte[]? content = null,
        uint flags = 0)
    {
        WebcilImageBuilder.R2RSectionSpec[] sections = sectionType.HasValue
            ? new[] { new WebcilImageBuilder.R2RSectionSpec(sectionType.Value, content ?? Array.Empty<byte>()) }
            : Array.Empty<WebcilImageBuilder.R2RSectionSpec>();
        return WebcilImageBuilder.BuildRawWebcil(
            majorVersion,
            minorVersion,
            sections,
            r2rFlags: flags);
    }

    private static ReadyToRunReader CreateReader(
        byte[] image,
        ReadyToRunValidationMode validationMode = ReadyToRunValidationMode.Tolerant)
        => new(
            new WebcilImageReader(image),
            new NativeReader(new MemoryStream(image)),
            options: new ReadyToRunReaderOptions(validationMode));

    private static int FindReadyToRunHeader(byte[] image)
    {
        ReadOnlySpan<byte> signature = stackalloc byte[] { 0x52, 0x54, 0x52, 0x00 };
        for (int i = 0; i <= image.Length - signature.Length; i++)
        {
            if (image.AsSpan(i, signature.Length).SequenceEqual(signature))
                return i;
        }

        throw new InvalidOperationException("ReadyToRun header signature was not found.");
    }
}
