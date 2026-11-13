// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace System.Reflection.Metadata.ReadyToRun;

/// <summary>A run of WebAssembly async resume-info fixups, as encoded in section 127.</summary>
public sealed class WasmAsyncResumeInfoChunk
{
    public uint Delta { get; }
    public uint Count { get; }
    public uint Stride { get; }

    internal WasmAsyncResumeInfoChunk(uint delta, uint count, uint stride)
        => (Delta, Count, Stride) = (delta, count, stride);
}

/// <summary>WebAssembly async resume-info fixup chunks.</summary>
public sealed class WasmAsyncResumeInfoTable
{
    public IReadOnlyList<WasmAsyncResumeInfoChunk> Chunks { get; }

    internal WasmAsyncResumeInfoTable(IReadOnlyList<WasmAsyncResumeInfoChunk> chunks) => Chunks = chunks;
}

public partial class ReadyToRunReader
{
    /// <summary>Reads the unsigned LEB128 chunk list in the WebAssembly async resume-info section.</summary>
    public WasmAsyncResumeInfoTable GetWasmAsyncResumeInfoTable(ReadyToRunSection section)
    {
        int offset = ValidateAndGetSectionOffset(section, Internal.Runtime.ReadyToRunSectionType.WasmAsyncResumeInfo, nameof(GetWasmAsyncResumeInfoTable));
        int end = checked(offset + section.Size);
        uint count = ReadWasmUnsigned(ref offset, end);
        if (count > (end - (long)offset) / 3)
            throw new BadImageFormatException("WebAssembly async resume-info chunk count exceeds its section.");
        var chunks = new List<WasmAsyncResumeInfoChunk>((int)count);
        for (uint i = 0; i < count; i++)
        {
            uint delta = ReadWasmUnsigned(ref offset, end);
            uint fixupCount = ReadWasmUnsigned(ref offset, end);
            uint stride = ReadWasmUnsigned(ref offset, end);
            if (fixupCount == 0 || stride != 2 * TargetPointerSize)
                throw new BadImageFormatException("Invalid WebAssembly async resume-info chunk.");
            chunks.Add(new WasmAsyncResumeInfoChunk(delta, fixupCount, stride));
        }
        return new WasmAsyncResumeInfoTable(chunks);
    }

    private uint ReadWasmUnsigned(ref int offset, int end)
    {
        uint value = 0;
        for (int shift = 0; shift <= 28; shift += 7)
        {
            if (offset >= end)
                throw new BadImageFormatException("Truncated WebAssembly unsigned LEB128 value.");
            byte b = _nativeReader.ReadByte(ref offset);
            if (shift == 28 && (b & 0xF0) != 0)
                throw new BadImageFormatException("WebAssembly unsigned LEB128 value overflows UInt32.");
            value |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return value;
        }
        throw new BadImageFormatException("Invalid WebAssembly unsigned LEB128 value.");
    }
}
