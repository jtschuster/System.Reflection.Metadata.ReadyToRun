using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Reflection.Metadata.ReadyToRun.x86;
using Amd64GcInfo = System.Reflection.Metadata.ReadyToRun.Amd64.GcInfo;

using Xunit;

namespace System.Reflection.Metadata.ReadyToRun.Tests;

public sealed class CompatibilityDecoderTests
{
    private const int HeaderOffset = 0;
    private const int DebugInfoEntryOffset = 32;
    private const int UnwindInfoOffset = 32;
    private const int GcInfoOffset = 40;
    private const int DebugInfoBlobOffset = DebugInfoEntryOffset + 1;

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(2, 0, 2)]
    [InlineData(9, 1, 2)]
    [InlineData(9, 2, 3)]
    [InlineData(10, 0, 3)]
    [InlineData(11, 0, 4)]
    [InlineData(20, 0, 4)]
    [InlineData(21, 0, 5)]
    public void Amd64GcInfoVersionBoundaries_SelectExpectedVersion(ushort majorVersion, ushort minorVersion, int expectedGcInfoVersion)
    {
        byte[] image = CreateAmd64GcInfoImage(majorVersion, minorVersion, BuildMinimalAmd64GcInfoPayload(expectedGcInfoVersion));

        using ReadyToRunReader reader = CreateReader(Machine.Amd64, image);
        Amd64GcInfo gcInfo = Assert.IsType<Amd64GcInfo>(reader.GetGcInfo((UnwindInfoRva)(uint)UnwindInfoOffset));

        Assert.Equal(expectedGcInfoVersion, gcInfo.Version);
    }

    [Fact]
    public void X86InfoHdr_GcInfoV5_ReusesNextOpcodeForAsyncBit()
    {
        byte[] payload = [0x80, 0x4F, 0x05, 0x00];

        InfoHdrSmall v4Header = DecodeInfoHdr(payload, version: 4);
        Assert.Equal(ReturnKinds.RT_Scalar, v4Header.ReturnKind);
        Assert.Equal<uint>(1, v4Header.NoGCRegionCnt);
        Assert.False(v4Header.IsAsync);

        InfoHdrSmall v5Header = DecodeInfoHdr(payload, version: 5);
        Assert.Equal(ReturnKinds.RT_Object, v5Header.ReturnKind);
        Assert.Equal<uint>(0, v5Header.NoGCRegionCnt);
        Assert.True(v5Header.IsAsync);
    }

    [Fact]
    public void DebugInfo_V15_UsesLegacyNibbleBounds()
    {
        byte[] boundsBlob = BuildLegacyBoundsBlob(nativeDelta: 5, ilOffsetDeltaFromMax: 3, sourceTypes: (uint)SourceTypes.StackEmpty);
        byte[] debugBlob = BuildClassicDebugBlob(boundsBlob, Array.Empty<byte>());

        using ReadyToRunReader reader = CreateReader(Machine.Amd64, CreateDebugInfoImage(majorVersion: 15, minorVersion: 0, debugBlob));
        DebugInfo debugInfo = reader.GetDebugInfo((DebugInfoOffset)(uint)DebugInfoEntryOffset);

        DebugInfoBoundsEntry entry = Assert.Single(debugInfo.Bounds);
        Assert.Equal<uint>(5, entry.NativeOffsetDelta);
        Assert.Equal<uint>(3, entry.ILOffsetDelta);
        Assert.Equal(SourceTypes.StackEmpty, entry.SourceTypes);
    }

    [Fact]
    public void DebugInfo_V16_UsesPackedBounds()
    {
        byte[] boundsBlob = BuildPackedBoundsBlob(majorVersion: 16, nativeDelta: 5, ilOffsetDeltaFromMax: 1, sourceTypes: 0b10);
        byte[] debugBlob = BuildClassicDebugBlob(boundsBlob, Array.Empty<byte>());

        using ReadyToRunReader reader = CreateReader(Machine.Amd64, CreateDebugInfoImage(majorVersion: 16, minorVersion: 0, debugBlob));
        DebugInfo debugInfo = reader.GetDebugInfo((DebugInfoOffset)(uint)DebugInfoEntryOffset);

        DebugInfoBoundsEntry entry = Assert.Single(debugInfo.Bounds);
        Assert.Equal<uint>(5, entry.NativeOffsetDelta);
        Assert.Equal<uint>(1, entry.ILOffsetDelta);
        Assert.Equal(SourceTypes.StackEmpty, entry.SourceTypes);
    }

    [Fact]
    public void DebugInfo_V17_UsesFatHeaderAndAsyncSourceBit()
    {
        byte[] boundsBlob = PadToLength(BuildPackedBoundsBlob(majorVersion: 17, nativeDelta: 5, ilOffsetDeltaFromMax: 1, sourceTypes: 0b101), 8);
        byte[] debugBlob = BuildFatDebugBlob(boundsBlob, Array.Empty<byte>(), uninstrumentedBoundsByteCount: 0, patchpointInfoByteCount: 0, richDebugInfoByteCount: 0, asyncInfoByteCount: 0);

        using ReadyToRunReader reader = CreateReader(Machine.Amd64, CreateDebugInfoImage(majorVersion: 17, minorVersion: 0, debugBlob));
        DebugInfo debugInfo = reader.GetDebugInfo((DebugInfoOffset)(uint)DebugInfoEntryOffset);

        DebugInfoBoundsEntry entry = Assert.Single(debugInfo.Bounds);
        Assert.Equal<uint>(5, entry.NativeOffsetDelta);
        Assert.Equal<uint>(1, entry.ILOffsetDelta);
        Assert.Equal(SourceTypes.CallInstruction | SourceTypes.Async, entry.SourceTypes);
    }

    [Fact]
    public void DebugInfo_FatAuxiliaryPayloadsRemainAvailableAsRawBytes()
    {
        byte[] debugBlob = Combine(
            BuildFatDebugBlob(Array.Empty<byte>(), Array.Empty<byte>(), uninstrumentedBoundsByteCount: 0, patchpointInfoByteCount: 0, richDebugInfoByteCount: 0, asyncInfoByteCount: 1),
            [0xA5]);

        using ReadyToRunReader reader = CreateReader(Machine.Amd64, CreateDebugInfoImage(majorVersion: 17, minorVersion: 0, debugBlob));
        DebugInfo debugInfo = reader.GetDebugInfo((DebugInfoOffset)(uint)DebugInfoEntryOffset);

        Assert.Empty(debugInfo.UninstrumentedBounds);
        Assert.Empty(debugInfo.PatchpointInfo);
        Assert.Empty(debugInfo.RichDebugInfo);
        Assert.Equal((byte)0xA5, Assert.Single(debugInfo.AsyncInfo));
    }

    [Fact]
    public void NativeVarInfo_V19_UsesLegacyImplicitArgumentFloor()
    {
        byte[] variablesBlob = PadToLength(BuildNativeVarBlobV19(), 8);
        byte[] debugBlob = BuildFatDebugBlob(Array.Empty<byte>(), variablesBlob, 0, 0, 0, 0);

        using ReadyToRunReader reader = CreateReader(Machine.Amd64, CreateDebugInfoImage(majorVersion: 19, minorVersion: 0, debugBlob));
        DebugInfo debugInfo = reader.GetDebugInfo((DebugInfoOffset)(uint)DebugInfoEntryOffset);

        NativeVarInfo entry = Assert.Single(debugInfo.Variables);
        Assert.Equal((int)ImplicitILArguments.MaxV19, entry.VariableNumber);
        Assert.Equal<uint>(10, entry.StartOffset);
        Assert.Equal((uint?)3, entry.RangeLength);
        Assert.Null(entry.CallReturnValueILOffset);
        Assert.Equal(VarLocType.VLT_REG, entry.VariableLocation.VarLocType);
        Assert.Equal(2, entry.VariableLocation.Data1);
    }

    [Fact]
    public void NativeVarInfo_V20_DecodesAsyncContinuation()
    {
        byte[] variablesBlob = PadToLength(BuildNativeVarBlobV20(), 8);
        byte[] debugBlob = BuildFatDebugBlob(Array.Empty<byte>(), variablesBlob, 0, 0, 0, 0);

        using ReadyToRunReader reader = CreateReader(Machine.Amd64, CreateDebugInfoImage(majorVersion: 20, minorVersion: 0, debugBlob));
        DebugInfo debugInfo = reader.GetDebugInfo((DebugInfoOffset)(uint)DebugInfoEntryOffset);

        NativeVarInfo entry = Assert.Single(debugInfo.Variables);
        Assert.Equal((int)ImplicitILArguments.AsyncContinuation, entry.VariableNumber);
        Assert.Equal<uint>(10, entry.StartOffset);
        Assert.Equal((uint?)3, entry.RangeLength);
        Assert.Null(entry.CallReturnValueILOffset);
    }

    [Fact]
    public void NativeVarInfo_V22_DecodesCallReturnValueRecord()
    {
        byte[] variablesBlob = PadToLength(BuildNativeVarBlobV22CallReturnValue(), 8);
        byte[] debugBlob = BuildFatDebugBlob(Array.Empty<byte>(), variablesBlob, 0, 0, 0, 0);

        using ReadyToRunReader reader = CreateReader(Machine.Amd64, CreateDebugInfoImage(majorVersion: 22, minorVersion: 0, debugBlob));
        DebugInfo debugInfo = reader.GetDebugInfo((DebugInfoOffset)(uint)DebugInfoEntryOffset);

        NativeVarInfo entry = Assert.Single(debugInfo.Variables);
        Assert.Equal((int)ImplicitILArguments.CallReturnValue, entry.VariableNumber);
        Assert.Equal<uint>(10, entry.StartOffset);
        Assert.Null(entry.RangeLength);
        Assert.Equal((uint?)33, entry.CallReturnValueILOffset);
    }

    [Fact]
    public void NativeVarInfo_InvalidVarLocThrowsBadImageFormatException()
    {
        byte[] variablesBlob = PadToLength(BuildMalformedNativeVarBlob(), 8);
        byte[] debugBlob = BuildFatDebugBlob(Array.Empty<byte>(), variablesBlob, 0, 0, 0, 0);

        using ReadyToRunReader reader = CreateReader(Machine.Amd64, CreateDebugInfoImage(majorVersion: 22, minorVersion: 0, debugBlob));
        Assert.Throws<BadImageFormatException>(() => reader.GetDebugInfo((DebugInfoOffset)(uint)DebugInfoEntryOffset));
    }

    [Fact]
    public void UnknownMajorVersion_GatesGcAndDebugSemanticDecoders()
    {
        using ReadyToRunReader debugReader = CreateReader(
            Machine.Amd64,
            CreateDebugInfoImage(majorVersion: 25, minorVersion: 0, [0x00]));
        Assert.Throws<NotSupportedException>(
            () => debugReader.GetDebugInfo((DebugInfoOffset)(uint)DebugInfoEntryOffset));

        using ReadyToRunReader gcReader = CreateReader(
            Machine.Amd64,
            CreateAmd64GcInfoImage(majorVersion: 25, minorVersion: 0, [0x00]));
        Assert.Throws<NotSupportedException>(
            () => gcReader.GetGcInfo((UnwindInfoRva)(uint)UnwindInfoOffset));
    }

    private static ReadyToRunReader CreateReader(Machine machine, byte[] image)
    {
        return new ReadyToRunReader(
            new SyntheticPlatformBinaryReader(machine),
            new NativeReader(new MemoryStream(image), leaveOpen: false));
    }

    private static byte[] CreateAmd64GcInfoImage(ushort majorVersion, ushort minorVersion, byte[] gcInfoPayload)
    {
        byte[] image = new byte[GcInfoOffset + gcInfoPayload.Length];
        WriteHeader(image, majorVersion, minorVersion);
        WriteBytes(image, UnwindInfoOffset, [0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
        WriteBytes(image, GcInfoOffset, gcInfoPayload);
        return image;
    }

    private static byte[] CreateDebugInfoImage(ushort majorVersion, ushort minorVersion, byte[] debugBlob)
    {
        byte[] image = new byte[DebugInfoBlobOffset + debugBlob.Length];
        WriteHeader(image, majorVersion, minorVersion);
        image[DebugInfoEntryOffset] = 0;
        WriteBytes(image, DebugInfoBlobOffset, debugBlob);
        return image;
    }

    private static byte[] BuildMinimalAmd64GcInfoPayload(int gcInfoVersion)
    {
        var writer = new BitWriter();
        writer.WriteBit(false); // slim header
        writer.WriteBit(false); // no stack base register

        if (gcInfoVersion is >= 2 and <= 3)
        {
            writer.WriteBits(0, 2); // RT_Scalar
        }

        writer.WriteVarLengthUnsigned(1, 8); // CodeLength
        writer.WriteVarLengthUnsigned(0, 2); // NumSafePoints
        writer.WriteBit(false); // no registers
        writer.WriteBit(false); // no stack slots / untracked slots
        return writer.ToArray();
    }

    private static byte[] BuildLegacyBoundsBlob(uint nativeDelta, uint ilOffsetDeltaFromMax, uint sourceTypes)
    {
        var writer = new NibbleWriter();
        writer.WriteUInt(1);
        writer.WriteUInt(nativeDelta);
        writer.WriteUInt(ilOffsetDeltaFromMax);
        writer.WriteUInt(sourceTypes);
        return writer.ToArray();
    }

    private static byte[] BuildPackedBoundsBlob(int majorVersion, uint nativeDelta, uint ilOffsetDeltaFromMax, uint sourceTypes)
    {
        int bitsForSourceType = majorVersion >= 17 ? 3 : 2;
        int bitsForNativeDelta = 9;
        int bitsForILOffset = 1;

        var nibbleWriter = new NibbleWriter();
        nibbleWriter.WriteUInt(1);
        nibbleWriter.WriteUInt((uint)(bitsForNativeDelta - 1));
        nibbleWriter.WriteUInt((uint)(bitsForILOffset - 1));

        ulong mappingData =
            sourceTypes |
            ((ulong)nativeDelta << bitsForSourceType) |
            ((ulong)ilOffsetDeltaFromMax << (bitsForSourceType + bitsForNativeDelta));

        var bitWriter = new BitWriter();
        bitWriter.WriteBits(mappingData, bitsForSourceType + bitsForNativeDelta + bitsForILOffset);

        return Combine(nibbleWriter.ToArray(), bitWriter.ToArray());
    }

    private static byte[] BuildClassicDebugBlob(byte[] boundsBlob, byte[] variablesBlob)
    {
        var headerWriter = new NibbleWriter();
        headerWriter.WriteUInt((uint)boundsBlob.Length);
        headerWriter.WriteUInt((uint)variablesBlob.Length);
        return Combine(headerWriter.ToArray(), boundsBlob, variablesBlob);
    }

    private static byte[] BuildFatDebugBlob(
        byte[] boundsBlob,
        byte[] variablesBlob,
        uint uninstrumentedBoundsByteCount,
        uint patchpointInfoByteCount,
        uint richDebugInfoByteCount,
        uint asyncInfoByteCount)
    {
        var headerWriter = new NibbleWriter();
        headerWriter.WriteUInt(0);
        headerWriter.WriteUInt((uint)boundsBlob.Length);
        headerWriter.WriteUInt((uint)variablesBlob.Length);
        headerWriter.WriteUInt(uninstrumentedBoundsByteCount);
        headerWriter.WriteUInt(patchpointInfoByteCount);
        headerWriter.WriteUInt(richDebugInfoByteCount);
        headerWriter.WriteUInt(asyncInfoByteCount);
        return Combine(headerWriter.ToArray(), boundsBlob, variablesBlob);
    }

    private static byte[] BuildNativeVarBlobV19()
    {
        var writer = new NibbleWriter();
        writer.WriteUInt(1);
        writer.WriteUInt(10);
        writer.WriteUInt(3);
        writer.WriteUInt(0);
        writer.WriteUInt((uint)VarLocType.VLT_REG);
        writer.WriteUInt(2);
        return writer.ToArray();
    }

    private static byte[] BuildNativeVarBlobV20()
    {
        var writer = new NibbleWriter();
        writer.WriteUInt(1);
        writer.WriteUInt(10);
        writer.WriteUInt(3);
        writer.WriteUInt(1);
        writer.WriteUInt((uint)VarLocType.VLT_REG);
        writer.WriteUInt(2);
        return writer.ToArray();
    }

    private static byte[] BuildNativeVarBlobV22CallReturnValue()
    {
        var writer = new NibbleWriter();
        writer.WriteUInt(1);
        writer.WriteUInt(1);
        writer.WriteUInt(10);
        writer.WriteUInt(33);
        writer.WriteUInt((uint)VarLocType.VLT_REG);
        writer.WriteUInt(2);
        return writer.ToArray();
    }

    private static byte[] BuildMalformedNativeVarBlob()
    {
        var writer = new NibbleWriter();
        writer.WriteUInt(1);
        writer.WriteUInt(1);
        writer.WriteUInt(10);
        writer.WriteUInt(33);
        writer.WriteUInt((uint)VarLocType.VLT_INVALID);
        return writer.ToArray();
    }

    private static InfoHdrSmall DecodeInfoHdr(byte[] payload, int version)
    {
        using var reader = new NativeReader(new MemoryStream(payload), leaveOpen: false);
        int offset = 0;
        return InfoHdrDecoder.DecodeHeader(reader, ref offset, codeLength: 10, version);
    }

    private static void WriteHeader(byte[] image, ushort majorVersion, ushort minorVersion)
    {
        int offset = HeaderOffset;
        WriteUInt32(image, ref offset, ReadyToRunHeader.READYTORUN_SIGNATURE);
        WriteUInt16(image, ref offset, majorVersion);
        WriteUInt16(image, ref offset, minorVersion);
        WriteUInt32(image, ref offset, 0);
        WriteInt32(image, ref offset, 0);
    }

    private static void WriteBytes(byte[] image, int offset, byte[] data)
    {
        Array.Copy(data, 0, image, offset, data.Length);
    }

    private static void WriteUInt32(byte[] image, ref int offset, uint value)
    {
        image[offset++] = (byte)value;
        image[offset++] = (byte)(value >> 8);
        image[offset++] = (byte)(value >> 16);
        image[offset++] = (byte)(value >> 24);
    }

    private static void WriteUInt16(byte[] image, ref int offset, ushort value)
    {
        image[offset++] = (byte)value;
        image[offset++] = (byte)(value >> 8);
    }

    private static void WriteInt32(byte[] image, ref int offset, int value)
        => WriteUInt32(image, ref offset, unchecked((uint)value));

    private static byte[] PadToLength(byte[] data, int length)
    {
        if (data.Length >= length)
        {
            return data;
        }

        byte[] padded = new byte[length];
        Array.Copy(data, padded, data.Length);
        return padded;
    }

    private static byte[] Combine(params byte[][] segments)
    {
        int totalLength = 0;
        foreach (byte[] segment in segments)
        {
            totalLength += segment.Length;
        }

        byte[] combined = new byte[totalLength];
        int offset = 0;
        foreach (byte[] segment in segments)
        {
            Array.Copy(segment, 0, combined, offset, segment.Length);
            offset += segment.Length;
        }

        return combined;
    }

    private sealed class SyntheticPlatformBinaryReader(Machine machine) : IPlatformBinaryReader
    {
        public Machine Machine => machine;

        public int GetOffset(int rva) => rva;

        public bool TryGetReadyToRunHeader(out int rva, out bool isComposite)
        {
            rva = 0;
            isComposite = false;
            return true;
        }

        public MetadataReader GetStandaloneAssemblyMetadata()
            => throw new NotSupportedException();

        public MetadataReader GetManifestAssemblyMetadata(int offset, int size)
            => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class NibbleWriter
    {
        private readonly List<byte> _nibbles = new();

        public void WriteUInt(uint value)
        {
            Span<byte> scratch = stackalloc byte[16];
            int count = 0;
            do
            {
                scratch[count++] = (byte)(value & 0x7);
                value >>= 3;
            }
            while (value != 0);

            for (int i = count - 1; i >= 0; i--)
            {
                byte nibble = scratch[i];
                if (i != 0)
                {
                    nibble |= 0x8;
                }

                _nibbles.Add(nibble);
            }
        }

        public byte[] ToArray()
        {
            byte[] bytes = new byte[(_nibbles.Count + 1) / 2];
            for (int i = 0; i < _nibbles.Count; i++)
            {
                int byteIndex = i / 2;
                if ((i & 1) == 0)
                {
                    bytes[byteIndex] = _nibbles[i];
                }
                else
                {
                    bytes[byteIndex] |= (byte)(_nibbles[i] << 4);
                }
            }

            return bytes;
        }
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = new();
        private int _bitOffset;

        public void WriteBit(bool value) => WriteBits(value ? 1u : 0u, 1);

        public void WriteBits(ulong value, int bitCount)
        {
            for (int i = 0; i < bitCount; i++)
            {
                bool bit = ((value >> i) & 1UL) != 0;
                int byteIndex = _bitOffset / 8;
                int bitIndex = _bitOffset % 8;
                if (byteIndex == _bytes.Count)
                {
                    _bytes.Add(0);
                }

                if (bit)
                {
                    _bytes[byteIndex] |= (byte)(1 << bitIndex);
                }

                _bitOffset++;
            }
        }

        public void WriteVarLengthUnsigned(uint value, int valueBitCount)
        {
            uint mask = (1u << valueBitCount) - 1;
            do
            {
                uint chunk = value & mask;
                value >>= valueBitCount;
                bool hasMore = value != 0;
                WriteBits(chunk | (hasMore ? (1u << valueBitCount) : 0u), valueBitCount + 1);
            }
            while (value != 0);
        }

        public byte[] ToArray() => _bytes.ToArray();
    }
}
