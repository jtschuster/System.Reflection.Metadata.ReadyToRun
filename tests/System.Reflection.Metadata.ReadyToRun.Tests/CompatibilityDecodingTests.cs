// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;

using Internal.ReadyToRunConstants;
using Internal.Runtime;

using Xunit;

namespace System.Reflection.Metadata.ReadyToRun.Tests;

public sealed class CompatibilityDecodingTests
{
    [Fact]
    public void ConstantsTrackPreviewFormat24AndPreserveHistoricalValues()
    {
        Assert.Equal((ushort)24, ReadyToRunHeaderConstants.CurrentMajorVersion);
        Assert.Equal((ushort)0, ReadyToRunHeaderConstants.CurrentMinorVersion);
        Assert.Equal(ReadyToRunHeaderConstants.CurrentMajorVersion, ReadyToRunHeader.MAXIMUM_SUPPORTED_MAJOR_VERSION);
        Assert.Equal(ReadyToRunHeaderConstants.CurrentMinorVersion, ReadyToRunHeader.MAXIMUM_SUPPORTED_MINOR_VERSION);

        Assert.Equal(0x80u, (uint)ReadyToRunMethodSigFlags.READYTORUN_METHOD_SIG_UpdateContext);
        Assert.Equal(0x100u, (uint)ReadyToRunMethodSigFlags.READYTORUN_METHOD_SIG_AsyncVariant);

        Assert.Equal(0x33, (int)ReadyToRunFixupKind.Check_VirtualFunctionOverride);
        Assert.Equal(0x38, (int)ReadyToRunFixupKind.ResumptionStubEntryPoint);
        Assert.Equal(0x39, (int)ReadyToRunFixupKind.InjectStringThunks);

        Assert.Equal(0x27, (int)ReadyToRunHelper.ThrowExact);
        Assert.Equal(0x117, (int)ReadyToRunHelper.InitInstClass);
        Assert.Equal(0x118, (int)ReadyToRunHelper.R2RToInterpreter);
    }

    [Fact]
    public void UpdateContext_IsVersionGatedAroundFivePointFour()
    {
        byte[] bytes = EncodeMethodSignature(
            (uint)ReadyToRunMethodSigFlags.READYTORUN_METHOD_SIG_UpdateContext,
            moduleIndex: 3,
            rid: 7);

        Assert.Throws<NotSupportedException>(() => DecodeMethod(bytes, ReadyToRunSignatureDecodingOptions.Strict(5, 3)));

        MethodSignature tolerant = MethodSignature.FromSignature(
            DecodeMethod(bytes, ReadyToRunSignatureDecodingOptions.Tolerant(5, 3)).Signature);
        Assert.Equal(3, tolerant.ModuleIndex);
        Assert.Equal(7, tolerant.Rid);

        MethodSignature strict = MethodSignature.FromSignature(
            DecodeMethod(bytes, ReadyToRunSignatureDecodingOptions.Strict(5, 4)).Signature);
        Assert.Equal(3, strict.ModuleIndex);
        Assert.Equal(7, strict.Rid);
    }

    [Fact]
    public void InjectStringThunks_IsVersionGatedAtEighteenPointSix()
    {
        byte[] bytes = EncodeInjectStringThunksFixup(("alpha", 0x12345678), ("beta", 0x01020304));

        Assert.Throws<NotSupportedException>(() => DecodeFixup(bytes, ReadyToRunSignatureDecodingOptions.Strict(18, 5)));

        R2RFixupSignature tolerant = R2RFixupSignature.FromSignature(
            DecodeFixup(bytes, ReadyToRunSignatureDecodingOptions.Tolerant(18, 5)).Signature);
        Assert.IsType<R2RInjectStringThunksFixupPayload>(tolerant.Payload);

        R2RFixupSignature strict = R2RFixupSignature.FromSignature(
            DecodeFixup(bytes, ReadyToRunSignatureDecodingOptions.Strict(18, 6)).Signature);
        R2RInjectStringThunksFixupPayload payload = Assert.IsType<R2RInjectStringThunksFixupPayload>(strict.Payload);
        Assert.Collection(
            payload.Entries,
            entry =>
            {
                Assert.Equal("alpha", entry.LookupString);
                Assert.Equal(0x12345678, entry.ThunkRva);
            },
            entry =>
            {
                Assert.Equal("beta", entry.LookupString);
                Assert.Equal(0x01020304, entry.ThunkRva);
            });
    }

    [Fact]
    public void Helpers_AreVersionGatedAndUnknownIdsRemainNumericInTolerantMode()
    {
        byte[] currentHelper = EncodeHelperFixup((uint)ReadyToRunHelper.R2RToInterpreter);

        Assert.Throws<NotSupportedException>(() => DecodeFixup(currentHelper, ReadyToRunSignatureDecodingOptions.Strict(18, 6)));

        R2RFixupSignature current = R2RFixupSignature.FromSignature(
            DecodeFixup(currentHelper, ReadyToRunSignatureDecodingOptions.Strict(18, 7)).Signature);
        R2RHelperFixupPayload currentPayload = Assert.IsType<R2RHelperFixupPayload>(current.Payload);
        Assert.Equal((uint)ReadyToRunHelper.R2RToInterpreter, currentPayload.RawHelperId);
        Assert.Equal(ReadyToRunHelper.R2RToInterpreter, currentPayload.HelperId);

        byte[] unknownHelper = EncodeHelperFixup(0x0200);
        Assert.Throws<NotSupportedException>(() => DecodeFixup(unknownHelper, ReadyToRunSignatureDecodingOptions.Strict(24, 0)));

        R2RFixupSignature tolerantUnknown = R2RFixupSignature.FromSignature(
            DecodeFixup(unknownHelper, ReadyToRunSignatureDecodingOptions.Tolerant(24, 0)).Signature);
        R2RHelperFixupPayload unknownPayload = Assert.IsType<R2RHelperFixupPayload>(tolerantUnknown.Payload);
        Assert.Equal(0x0200u, unknownPayload.RawHelperId);
    }

    [Fact]
    public void UnknownFixupKinds_AreOpaqueInTolerantModeAndRejectedInStrictMode()
    {
        byte[] bytes = { 0x7A, 0xAA, 0xBB };

        Assert.Throws<NotSupportedException>(() => DecodeFixup(bytes, ReadyToRunSignatureDecodingOptions.Strict(24, 0)));

        R2RSignatureDecodeResult tolerant = DecodeFixup(bytes, ReadyToRunSignatureDecodingOptions.Tolerant(24, 0));
        Assert.Equal(1, tolerant.EndOffset);

        R2RFixupSignature fixup = R2RFixupSignature.FromSignature(tolerant.Signature);
        Assert.Equal((ReadyToRunFixupKind)0x7A, fixup.Kind);
        R2ROpaqueFixupPayload payload = Assert.IsType<R2ROpaqueFixupPayload>(fixup.Payload);
        Assert.Equal(1, payload.PayloadOffset);
    }

    [Fact]
    public void TruncatedMethodAndFixupEncodingsThrowBadImageFormatException()
    {
        byte[] truncatedMethod = { (byte)ReadyToRunMethodSigFlags.READYTORUN_METHOD_SIG_OwnerType };
        Assert.Throws<BadImageFormatException>(() => DecodeMethod(truncatedMethod, ReadyToRunSignatureDecodingOptions.Strict(24, 0)));

        byte[] truncatedFixup = { (byte)ReadyToRunFixupKind.InjectStringThunks, (byte)'x', 0x00, 0x34, 0x12 };
        Assert.Throws<BadImageFormatException>(() => DecodeFixup(truncatedFixup, ReadyToRunSignatureDecodingOptions.Strict(24, 0)));
    }

    [Fact]
    public void InjectStringThunks_InvalidUtf8ThrowsBadImageFormatException()
    {
        byte[] bytes =
        {
            (byte)ReadyToRunFixupKind.InjectStringThunks,
            0xC3, 0x28, 0x00,
            0x78, 0x56, 0x34, 0x12,
            0x00,
        };

        R2RSignature signature = DecodeFixup(
            bytes,
            ReadyToRunSignatureDecodingOptions.Strict(24, 0)).Signature;

        Assert.Throws<BadImageFormatException>(() => R2RFixupSignature.FromSignature(signature));
    }

    private static R2RSignatureDecodeResult DecodeMethod(byte[] bytes, ReadyToRunSignatureDecodingOptions options)
    {
        using var reader = new NativeReader(new MemoryStream(bytes), leaveOpen: false);
        return RawSignatureDecoder.DecodeMethodSignatureWithEndOffset(reader, 0, targetPointerSize: 8, options);
    }

    private static R2RSignatureDecodeResult DecodeFixup(byte[] bytes, ReadyToRunSignatureDecodingOptions options)
    {
        using var reader = new NativeReader(new MemoryStream(bytes), leaveOpen: false);
        return RawSignatureDecoder.DecodeFixupSignatureWithEndOffset(reader, 0, targetPointerSize: 8, options);
    }

    private static byte[] EncodeMethodSignature(uint flags, uint? moduleIndex = null, uint rid = 1)
    {
        var bytes = new List<byte>();
        WriteCompressedUInt(bytes, flags);
        if (moduleIndex.HasValue)
            WriteCompressedUInt(bytes, moduleIndex.Value);
        WriteCompressedUInt(bytes, rid);
        return PadForLookahead(bytes);
    }

    private static byte[] EncodeHelperFixup(uint helperId)
    {
        var bytes = new List<byte> { (byte)ReadyToRunFixupKind.Helper };
        WriteCompressedUInt(bytes, helperId);
        return PadForLookahead(bytes);
    }

    private static byte[] EncodeInjectStringThunksFixup(params (string LookupString, int ThunkRva)[] entries)
    {
        var bytes = new List<byte> { (byte)ReadyToRunFixupKind.InjectStringThunks };
        foreach ((string lookupString, int thunkRva) in entries)
        {
            bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(lookupString));
            bytes.Add(0);
            WriteInt32(bytes, thunkRva);
        }
        bytes.Add(0);
        return PadForLookahead(bytes);
    }

    private static void WriteCompressedUInt(List<byte> bytes, uint value)
    {
        if (value <= 0x7F)
        {
            bytes.Add((byte)value);
            return;
        }

        if (value <= 0x3FFF)
        {
            bytes.Add((byte)(0x80 | (value >> 8)));
            bytes.Add((byte)value);
            return;
        }

        bytes.Add((byte)(0xC0 | (value >> 24)));
        bytes.Add((byte)(value >> 16));
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)value);
    }

    private static void WriteInt32(List<byte> bytes, int value)
    {
        bytes.Add((byte)value);
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)(value >> 16));
        bytes.Add((byte)(value >> 24));
    }

    private static byte[] PadForLookahead(List<byte> bytes)
    {
        bytes.Add(0);
        bytes.Add(0);
        bytes.Add(0);
        bytes.Add(0);
        return bytes.ToArray();
    }
}
