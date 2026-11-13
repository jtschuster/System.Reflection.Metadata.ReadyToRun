// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Reflection.PortableExecutable;


namespace System.Reflection.Metadata.ReadyToRun;

/// <summary>WebAssembly frame size and virtual instruction-pointer span.</summary>
internal sealed class WasmUnwindInfo : BaseUnwindInfo
{
    public uint FrameSize { get; }
    public uint VirtualIPCount { get; }

    internal WasmUnwindInfo(uint frameSize, uint virtualIPCount, int size)
        => (FrameSize, VirtualIPCount, Size) = (frameSize, virtualIPCount, size);
}

public partial class ReadyToRunReader
{
    /// <summary>
    /// Resolve an <see cref="UnwindInfoRva"/> to its parsed unwind information.
    /// </summary>
    /// <remarks>
    /// Crossgen2 emitter: per-RuntimeFunction unwind data attached to <c>MethodWithGCInfo</c>
    /// (for x64/arm64 this is the PE <c>.pdata</c>/<c>xdata</c> referenced by <c>RuntimeFunctionsTableNode</c> entries).
    /// </remarks>
    internal BaseUnwindInfo GetUnwindInfo(UnwindInfoRva handle)
    {
        EnsureSemanticDecodingSupported(nameof(GetUnwindInfo));

        int unwindOffset = (int)GetOffsetForRVA((ImageRVA)(uint)handle);
        BaseUnwindInfo result = Machine switch
        {
            Machine.I386 => new x86.UnwindInfo(_nativeReader, unwindOffset),
            Machine.Amd64 => new Amd64.UnwindInfo(_nativeReader, unwindOffset),
            Machine.ArmThumb2 => new Arm.UnwindInfo(_nativeReader, unwindOffset),
            Machine.Arm64 => new Arm64.UnwindInfo(_nativeReader, unwindOffset),
            Machine.LoongArch64 => new LoongArch64.UnwindInfo(_nativeReader, unwindOffset),
            Machine.RiscV64 => new RiscV64.UnwindInfo(_nativeReader, unwindOffset),
            WasmMachine.Wasm32 => ReadWasmUnwindInfo(handle, unwindOffset),
            _ => throw new NotSupportedException($"Unsupported unwind info machine '{Machine}'."),
        };

        return result;
    }

    private WasmUnwindInfo ReadWasmUnwindInfo(UnwindInfoRva handle, int offset)
    {
        int start = offset;
        int end = (int)System.Math.Min(_nativeReader.Length, int.MaxValue);
        uint frameSize = ReadWasmUnsigned(ref offset, end);
        uint encodedCount = ReadWasmUnsigned(ref offset, end);
        if (encodedCount > uint.MaxValue / 2)
            throw new BadImageFormatException("WebAssembly virtual instruction-pointer count overflows UInt32.");
        ValidateAndGetRvaRange((uint)handle, offset - start, "WebAssembly unwind information");
        return new WasmUnwindInfo(frameSize, encodedCount * 2, offset - start);
    }
}
