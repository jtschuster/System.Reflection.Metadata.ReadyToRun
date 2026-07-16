// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reflection.PortableExecutable;


namespace System.Reflection.Metadata.ReadyToRun;

/// <summary>
/// Debug information for a single runtime function, parsed without depending on the
/// legacy <see cref="RuntimeFunction"/> or <see cref="ReadyToRunMethod"/> types.
/// Contains sequence-point bounds and native variable locations.
/// </summary>
/// <remarks>
/// Crossgen2 emitter: <c>DebugInfoTableNode (per-method DebugInfo blob built by DebugInfoBuilder)</c>.
/// </remarks>
public sealed class DebugInfo
{
    /// <summary>Sequence-point bounds mapping native offsets to IL offsets.</summary>
    public IReadOnlyList<DebugInfoBoundsEntry> Bounds { get; }

    /// <summary>Native variable location information.</summary>
    public IReadOnlyList<NativeVarInfo> Variables { get; }

    /// <summary>Raw uninstrumented-bounds payload from a fat debug-info record.</summary>
    public ImmutableArray<byte> UninstrumentedBounds { get; }

    /// <summary>Raw patchpoint payload from a fat debug-info record.</summary>
    public ImmutableArray<byte> PatchpointInfo { get; }

    /// <summary>Raw rich-debug-info payload from a fat debug-info record.</summary>
    public ImmutableArray<byte> RichDebugInfo { get; }

    /// <summary>Raw async-info payload from a fat debug-info record.</summary>
    public ImmutableArray<byte> AsyncInfo { get; }

    internal DebugInfo(
        IReadOnlyList<DebugInfoBoundsEntry> bounds,
        IReadOnlyList<NativeVarInfo> variables,
        ImmutableArray<byte> uninstrumentedBounds,
        ImmutableArray<byte> patchpointInfo,
        ImmutableArray<byte> richDebugInfo,
        ImmutableArray<byte> asyncInfo)
    {
        Bounds = bounds;
        Variables = variables;
        UninstrumentedBounds = uninstrumentedBounds;
        PatchpointInfo = patchpointInfo;
        RichDebugInfo = richDebugInfo;
        AsyncInfo = asyncInfo;
    }
}

public partial class ReadyToRunReader
{
    private readonly Dictionary<(DebugInfoOffset Offset, int StartOffset, int EndOffset), DebugInfo> _debugInfoCache = new();
    private Dictionary<DebugInfoOffset, (int StartOffset, int EndOffset)> _debugInfoRanges;

    /// <summary>
    /// Parse debug information from an R2R image at the given file offset.
    /// This is a standalone parser that does not depend on <see cref="RuntimeFunction"/>.
    /// </summary>
    /// <param name="offset">File offset pointing into the debug info NativeArray.</param>
    /// <returns>Parsed debug info.</returns>
    public DebugInfo GetDebugInfo(DebugInfoOffset offset)
    {
        EnsureSemanticDecodingSupported(nameof(GetDebugInfo));

        int containingStartOffset = 0;
        int containingEndOffset = _nativeReader.Length > int.MaxValue
            ? int.MaxValue
            : (int)_nativeReader.Length;
        if (_debugInfoRanges is not null
            && _debugInfoRanges.TryGetValue(offset, out var registeredRange))
        {
            containingStartOffset = registeredRange.StartOffset;
            containingEndOffset = registeredRange.EndOffset;
        }

        var cacheKey = (offset, containingStartOffset, containingEndOffset);
        if (_debugInfoCache.TryGetValue(cacheKey, out DebugInfo cached))
        {
            return cached;
        }

        NativeReader imageReader = ImageReader;
        Machine machine = Machine;
        ReadyToRunFormatProfile profile = FormatProfile;
        uint entryOffset = (uint)offset;
        if (entryOffset > int.MaxValue)
            throw new BadImageFormatException("Debug info entry offset exceeds the supported image range.");

        // Resolve the NativeArray indirection (lookback encoding)
        uint lookback = 0;
        uint debugInfoOffset = imageReader.DecodeUnsigned(entryOffset, ref lookback);
        if (debugInfoOffset > containingEndOffset)
            throw new BadImageFormatException("Debug info lookback encoding extends beyond its containing section.");

        if (lookback != 0)
        {
            if (entryOffset < containingStartOffset
                || lookback > entryOffset - (uint)containingStartOffset)
            {
                throw new BadImageFormatException("Debug info lookback points outside its containing section.");
            }
            debugInfoOffset = entryOffset - lookback;
        }
        if (debugInfoOffset > int.MaxValue)
            throw new BadImageFormatException("Debug info payload offset exceeds the supported image range.");

        NibbleReader reader = new NibbleReader(
            imageReader,
            (int)debugInfoOffset,
            containingEndOffset);

        uint boundsByteCountOrIndicator = reader.ReadUInt();

        uint boundsByteCount;
        uint variablesByteCount;
        uint uninstrumentedBoundsByteCount = 0;
        uint patchpointInfoByteCount = 0;
        uint richDebugInfoByteCount = 0;
        uint asyncInfoByteCount = 0;

        const int DebugInfoFat = 0;
        if (profile.UsesFatDebugInfo && boundsByteCountOrIndicator == DebugInfoFat)
        {
            boundsByteCount = reader.ReadUInt();
            variablesByteCount = reader.ReadUInt();
            uninstrumentedBoundsByteCount = reader.ReadUInt();
            patchpointInfoByteCount = reader.ReadUInt();
            richDebugInfoByteCount = reader.ReadUInt();
            asyncInfoByteCount = reader.ReadUInt();
        }
        else
        {
            boundsByteCount = boundsByteCountOrIndicator;
            variablesByteCount = reader.ReadUInt();
        }

        int boundsOffset = reader.GetNextByteOffset();
        long payloadByteCount =
            (long)boundsByteCount +
            variablesByteCount +
            uninstrumentedBoundsByteCount +
            patchpointInfoByteCount +
            richDebugInfoByteCount +
            asyncInfoByteCount;
        if (payloadByteCount > containingEndOffset - (long)boundsOffset)
            throw new BadImageFormatException("Debug info payload extends outside its containing section.");
        if (payloadByteCount > int.MaxValue - (long)boundsOffset)
            throw new BadImageFormatException("Debug info payload exceeds the supported image range.");
        if (boundsByteCount > int.MaxValue ||
            variablesByteCount > int.MaxValue ||
            uninstrumentedBoundsByteCount > int.MaxValue ||
            patchpointInfoByteCount > int.MaxValue ||
            richDebugInfoByteCount > int.MaxValue ||
            asyncInfoByteCount > int.MaxValue)
        {
            throw new BadImageFormatException("Debug info payload is too large.");
        }

        int variablesOffset = checked(boundsOffset + (int)boundsByteCount);
        int uninstrumentedBoundsOffset = checked(variablesOffset + (int)variablesByteCount);
        int patchpointInfoOffset = checked(uninstrumentedBoundsOffset + (int)uninstrumentedBoundsByteCount);
        int richDebugInfoOffset = checked(patchpointInfoOffset + (int)patchpointInfoByteCount);
        int asyncInfoOffset = checked(richDebugInfoOffset + (int)richDebugInfoByteCount);

        var bounds = new List<DebugInfoBoundsEntry>();
        if (boundsByteCount > 0)
        {
            byte[] boundsBytes = ReadDebugInfoBytes(imageReader, boundsOffset, boundsByteCount);
            using var boundsReader = new NativeReader(new MemoryStream(boundsBytes, writable: false), leaveOpen: false);
            ParseBounds(boundsReader, profile, bounds);
        }

        var variables = new List<NativeVarInfo>();
        if (variablesByteCount > 0)
        {
            byte[] variableBytes = ReadDebugInfoBytes(imageReader, variablesOffset, variablesByteCount);
            using var variableReader = new NativeReader(new MemoryStream(variableBytes, writable: false), leaveOpen: false);
            ParseNativeVarInfo(variableReader, machine, profile.NativeVarInfoVersion, variables);
        }

        DebugInfo parsed = new DebugInfo(
            bounds,
            variables,
            ReadDebugInfoBytes(imageReader, uninstrumentedBoundsOffset, uninstrumentedBoundsByteCount).ToImmutableArray(),
            ReadDebugInfoBytes(imageReader, patchpointInfoOffset, patchpointInfoByteCount).ToImmutableArray(),
            ReadDebugInfoBytes(imageReader, richDebugInfoOffset, richDebugInfoByteCount).ToImmutableArray(),
            ReadDebugInfoBytes(imageReader, asyncInfoOffset, asyncInfoByteCount).ToImmutableArray());
        _debugInfoCache[cacheKey] = parsed;
        return parsed;

        static byte[] ReadDebugInfoBytes(NativeReader imageReader, int offset, uint byteCount)
        {
            if (byteCount > int.MaxValue)
                throw new BadImageFormatException("Debug info payload is too large.");

            byte[] bytes = new byte[(int)byteCount];
            imageReader.ReadSpanAt(ref offset, bytes);
            return bytes;
        }

        static void ParseBounds(
            NativeReader imageReader,
            ReadyToRunFormatProfile profile,
            List<DebugInfoBoundsEntry> bounds)
        {
            const int offset = 0;
            if (profile.UsesPackedDebugBounds)
            {
                NibbleReader reader = new NibbleReader(imageReader, offset);
                uint boundsEntryCount = reader.ReadUInt();
                if (boundsEntryCount == 0)
                    throw new BadImageFormatException("Packed debug bounds contain no entries.");
                uint encodedBitsForNativeDelta = reader.ReadUInt();
                uint encodedBitsForILOffsets = reader.ReadUInt();
                if (encodedBitsForNativeDelta == uint.MaxValue ||
                    encodedBitsForILOffsets == uint.MaxValue)
                {
                    throw new BadImageFormatException("Packed debug bounds use an invalid bit width.");
                }

                uint bitsForNativeDelta = encodedBitsForNativeDelta + 1;
                uint bitsForILOffsets = encodedBitsForILOffsets + 1;

                uint bitsForSourceType = profile.UsesFatDebugInfo ? 3u : 2u;
                ulong bitsPerEntryValue = (ulong)bitsForNativeDelta + bitsForILOffsets + bitsForSourceType;
                if (bitsForNativeDelta >= 64 ||
                    bitsForILOffsets >= 64 ||
                    bitsPerEntryValue > 64)
                {
                    throw new BadImageFormatException("Packed debug bounds use an invalid bit width.");
                }
                uint bitsPerEntry = (uint)bitsPerEntryValue;

                ulong bitsMeaningfulMask = bitsPerEntry == 64
                    ? ulong.MaxValue
                    : (1UL << (int)bitsPerEntry) - 1;
                int offsetOfActualBoundsData = reader.GetNextByteOffset();
                long bitOffset = (long)offsetOfActualBoundsData * 8;

                for (uint curBoundsProcessed = 0; curBoundsProcessed < boundsEntryCount; curBoundsProcessed++)
                {
                    ulong mappingDataEncoded = 0;
                    for (int bitIndex = 0; bitIndex < bitsPerEntry; bitIndex++, bitOffset++)
                    {
                        long byteOffsetValue = bitOffset >> 3;
                        if (byteOffsetValue > int.MaxValue)
                            throw new BadImageFormatException("Packed debug bounds exceed the supported image range.");
                        int byteOffset = (int)byteOffsetValue;
                        int bitInByte = (int)(bitOffset & 7);
                        ulong bit = ((ulong)imageReader[byteOffset] >> bitInByte) & 1;
                        mappingDataEncoded |= bit << bitIndex;
                    }

                    mappingDataEncoded &= bitsMeaningfulMask;
                    SourceTypes sourceTypes = 0;
                    if ((mappingDataEncoded & 0x1) != 0)
                        sourceTypes |= SourceTypes.CallInstruction;
                    if ((mappingDataEncoded & 0x2) != 0)
                        sourceTypes |= SourceTypes.StackEmpty;
                    if (profile.UsesFatDebugInfo && (mappingDataEncoded & 0x4) != 0)
                        sourceTypes |= SourceTypes.Async;

                    mappingDataEncoded >>= (int)bitsForSourceType;
                    uint nativeOffsetDelta = (uint)(mappingDataEncoded & ((1UL << (int)bitsForNativeDelta) - 1));

                    mappingDataEncoded >>= (int)bitsForNativeDelta;
                    uint ilOffsetDelta = (uint)mappingDataEncoded;

                    bounds.Add(new DebugInfoBoundsEntry
                    {
                        NativeOffsetDelta = nativeOffsetDelta,
                        ILOffsetDelta = ilOffsetDelta,
                        SourceTypes = sourceTypes
                    });
                }
            }
            else
            {
                NibbleReader reader = new NibbleReader(imageReader, offset);
                uint boundsEntryCount = reader.ReadUInt();
                if (boundsEntryCount == 0)
                    throw new BadImageFormatException("Legacy debug bounds contain no entries.");
                if (boundsEntryCount > int.MaxValue)
                    throw new BadImageFormatException("Legacy debug bounds contain too many entries.");

                for (int i = 0; i < boundsEntryCount; ++i)
                {
                    var entry = new DebugInfoBoundsEntry()
                    {
                        NativeOffsetDelta = reader.ReadUInt(),
                        ILOffsetDelta = reader.ReadUInt(),
                        SourceTypes = (SourceTypes)reader.ReadUInt()
                    };
                    bounds.Add(entry);
                }
            }
        }

        static void ParseNativeVarInfo(
            NativeReader imageReader,
            Machine machine,
            int nativeVarInfoVersion,
            List<NativeVarInfo> variables)
        {
            NibbleReader reader = new NibbleReader(imageReader, offset: 0);
            uint nativeVarCount = reader.ReadUInt();
            if (nativeVarCount > int.MaxValue)
                throw new BadImageFormatException("Debug info contains too many native variable records.");
            int implicitILAdjust = nativeVarInfoVersion switch
            {
                >= 22 => (int)ImplicitILArguments.MaxV22,
                >= 20 => (int)ImplicitILArguments.MaxV20,
                _ => (int)ImplicitILArguments.MaxV19,
            };

            for (int i = 0; i < nativeVarCount; ++i)
            {
                var entry = new NativeVarInfo();

                if (nativeVarInfoVersion >= 22)
                {
                    entry.VariableNumber = ReadVariableNumber(reader, implicitILAdjust);
                    entry.StartOffset = reader.ReadUInt();

                    if (entry.VariableNumber == (int)ImplicitILArguments.CallReturnValue)
                    {
                        entry.CallReturnValueILOffset = reader.ReadUInt();
                    }
                    else
                    {
                        entry.RangeLength = reader.ReadUInt();
                    }
                }
                else
                {
                    entry.StartOffset = reader.ReadUInt();
                    entry.RangeLength = reader.ReadUInt();
                    entry.VariableNumber = ReadVariableNumber(reader, implicitILAdjust);
                }

                var varLoc = new VarLoc();
                varLoc.VarLocType = (VarLocType)reader.ReadUInt();
                switch (varLoc.VarLocType)
                {
                    case VarLocType.VLT_REG:
                    case VarLocType.VLT_REG_FP:
                    case VarLocType.VLT_REG_BYREF:
                        varLoc.Data1 = (int)reader.ReadUInt();
                        break;
                    case VarLocType.VLT_STK:
                    case VarLocType.VLT_STK_BYREF:
                        varLoc.Data1 = (int)reader.ReadUInt();
                        varLoc.Data2 = ReadEncodedStackOffset(reader, machine);
                        break;
                    case VarLocType.VLT_REG_REG:
                        varLoc.Data1 = (int)reader.ReadUInt();
                        varLoc.Data2 = (int)reader.ReadUInt();
                        break;
                    case VarLocType.VLT_REG_STK:
                        varLoc.Data1 = (int)reader.ReadUInt();
                        varLoc.Data2 = (int)reader.ReadUInt();
                        varLoc.Data3 = ReadEncodedStackOffset(reader, machine);
                        break;
                    case VarLocType.VLT_STK_REG:
                        varLoc.Data1 = ReadEncodedStackOffset(reader, machine);
                        varLoc.Data2 = (int)reader.ReadUInt();
                        varLoc.Data3 = (int)reader.ReadUInt();
                        break;
                    case VarLocType.VLT_STK2:
                        varLoc.Data1 = (int)reader.ReadUInt();
                        varLoc.Data2 = ReadEncodedStackOffset(reader, machine);
                        break;
                    case VarLocType.VLT_FPSTK:
                        varLoc.Data1 = (int)reader.ReadUInt();
                        break;
                    case VarLocType.VLT_FIXED_VA:
                        varLoc.Data1 = (int)reader.ReadUInt();
                        break;
                    default:
                        throw new BadImageFormatException("Unexpected var loc type");
                }

                entry.VariableLocation = varLoc;
                variables.Add(entry);
            }

            static int ReadVariableNumber(NibbleReader reader, int implicitILAdjust)
            {
                uint encodedVariableNumber = reader.ReadUInt();
                if (encodedVariableNumber > int.MaxValue)
                    throw new BadImageFormatException("Native variable number exceeds the supported range.");
                return (int)encodedVariableNumber + implicitILAdjust;
            }
        }

        static int ReadEncodedStackOffset(NibbleReader reader, Machine machine)
        {
            int offset = reader.ReadInt();
            if (machine == Machine.I386)
            {
                offset *= 4; // sizeof(DWORD)
            }

            return offset;
        }
    }

    private void RegisterDebugInfoRange(
        DebugInfoOffset offset,
        int containingStartOffset,
        int containingEndOffset)
    {
        uint rawOffset = (uint)offset;
        if (rawOffset < containingStartOffset || rawOffset >= containingEndOffset)
            throw new BadImageFormatException("Debug info entry offset is outside its containing section.");

        _debugInfoRanges ??= new Dictionary<DebugInfoOffset, (int, int)>();
        _debugInfoRanges[offset] = (containingStartOffset, containingEndOffset);
    }
}
