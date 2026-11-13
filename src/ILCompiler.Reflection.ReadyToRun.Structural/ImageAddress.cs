// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Reflection.Metadata.ReadyToRun;

/// <summary>File offset into the bytes of an image.</summary>
public readonly record struct ImageOffset(uint Value)
{
    public static explicit operator ImageOffset(uint value) => new(value);
    public static explicit operator ImageOffset(int value) => new(unchecked((uint)value));
    public static explicit operator uint(ImageOffset value) => value.Value;
    public static explicit operator int(ImageOffset value) => unchecked((int)value.Value);
}

/// <summary>Relative virtual address (RVA) encoded in an image.</summary>
public readonly record struct ImageRVA(uint Value)
{
    public static explicit operator ImageRVA(uint value) => new(value);
    public static explicit operator ImageRVA(int value) => new(unchecked((uint)value));
    public static explicit operator uint(ImageRVA value) => value.Value;
    public static explicit operator int(ImageRVA value) => unchecked((int)value.Value);
}

/// <summary>
/// Encoded code location from a ReadyToRun image. It is usually an RVA, but its interpretation is
/// target-dependent: ARM Thumb2 includes the Thumb bit, while WebAssembly uses a virtual
/// instruction-pointer index with bit 31 marking a funclet.
/// </summary>
public readonly record struct PCode(uint Value)
{
    public static explicit operator PCode(uint value) => new(value);
    public static explicit operator PCode(int value) => new(unchecked((uint)value));
    public static explicit operator uint(PCode value) => value.Value;
    public static explicit operator int(PCode value) => unchecked((int)value.Value);
}

/// <summary>RVA of a code region.</summary>
public readonly record struct CodeRva(uint Value)
{
    public static explicit operator CodeRva(uint value) => new(value);
    public static explicit operator CodeRva(int value) => new(unchecked((uint)value));
    public static explicit operator uint(CodeRva value) => value.Value;
    public static explicit operator int(CodeRva value) => unchecked((int)value.Value);
}

/// <summary>Method definition row identifier.</summary>
public readonly record struct MethodRid(uint Value)
{
    public static explicit operator MethodRid(uint value) => new(value);
    public static explicit operator MethodRid(int value) => new(unchecked((uint)value));
    public static explicit operator uint(MethodRid value) => value.Value;
    public static explicit operator int(MethodRid value) => unchecked((int)value.Value);
}

/// <summary>Type definition row identifier.</summary>
public readonly record struct TypeRid(uint Value)
{
    public static explicit operator TypeRid(uint value) => new(value);
    public static explicit operator TypeRid(int value) => new(unchecked((uint)value));
    public static explicit operator uint(TypeRid value) => value.Value;
    public static explicit operator int(TypeRid value) => unchecked((int)value.Value);
}

/// <summary>Zero-based index into the RuntimeFunctions table.</summary>
public readonly record struct RuntimeFunctionIndex(uint Value)
{
    public static explicit operator RuntimeFunctionIndex(uint value) => new(value);
    public static explicit operator RuntimeFunctionIndex(int value) => new(unchecked((uint)value));
    public static explicit operator uint(RuntimeFunctionIndex value) => value.Value;
    public static explicit operator int(RuntimeFunctionIndex value) => unchecked((int)value.Value);
}

/// <summary>RVA of exception-handling information.</summary>
public readonly record struct EHInfoRva(uint Value)
{
    public static explicit operator EHInfoRva(uint value) => new(value);
    public static explicit operator EHInfoRva(int value) => new(unchecked((uint)value));
    public static explicit operator uint(EHInfoRva value) => value.Value;
    public static explicit operator int(EHInfoRva value) => unchecked((int)value.Value);
}

/// <summary>RVA of unwind information.</summary>
public readonly record struct UnwindInfoRva(uint Value)
{
    public static explicit operator UnwindInfoRva(uint value) => new(value);
    public static explicit operator UnwindInfoRva(int value) => new(unchecked((uint)value));
    public static explicit operator uint(UnwindInfoRva value) => value.Value;
    public static explicit operator int(UnwindInfoRva value) => unchecked((int)value.Value);
}

/// <summary>Image offset of debug information.</summary>
public readonly record struct DebugInfoOffset(uint Value)
{
    public static explicit operator DebugInfoOffset(uint value) => new(value);
    public static explicit operator DebugInfoOffset(int value) => new(unchecked((uint)value));
    public static explicit operator uint(DebugInfoOffset value) => value.Value;
    public static explicit operator int(DebugInfoOffset value) => unchecked((int)value.Value);
}

/// <summary>RVA of the delay-load method thunk section.</summary>
public readonly record struct DelayLoadMethodThunkRva(uint Value)
{
    public static explicit operator DelayLoadMethodThunkRva(uint value) => new(value);
    public static explicit operator DelayLoadMethodThunkRva(int value) => new(unchecked((uint)value));
    public static explicit operator uint(DelayLoadMethodThunkRva value) => value.Value;
    public static explicit operator int(DelayLoadMethodThunkRva value) => unchecked((int)value.Value);
}

/// <summary>RVA of a ReadyToRun fixup signature.</summary>
public readonly record struct SignatureRva(uint Value)
{
    public static explicit operator SignatureRva(uint value) => new(value);
    public static explicit operator SignatureRva(int value) => new(unchecked((uint)value));
    public static explicit operator uint(SignatureRva value) => value.Value;
    public static explicit operator int(SignatureRva value) => unchecked((int)value.Value);
}

/// <summary>RVA of an import signature table.</summary>
public readonly record struct SignatureTableRva(uint Value)
{
    public static explicit operator SignatureTableRva(uint value) => new(value);
    public static explicit operator SignatureTableRva(int value) => new(unchecked((uint)value));
    public static explicit operator uint(SignatureTableRva value) => value.Value;
    public static explicit operator int(SignatureTableRva value) => unchecked((int)value.Value);
}

/// <summary>RVA of an import slot table.</summary>
public readonly record struct ImportSlotTableRva(uint Value)
{
    public static explicit operator ImportSlotTableRva(uint value) => new(value);
    public static explicit operator ImportSlotTableRva(int value) => new(unchecked((uint)value));
    public static explicit operator uint(ImportSlotTableRva value) => value.Value;
    public static explicit operator int(ImportSlotTableRva value) => unchecked((int)value.Value);
}

/// <summary>RVA of an auxiliary import data table.</summary>
public readonly record struct AuxiliaryDataTableRva(uint Value)
{
    public static explicit operator AuxiliaryDataTableRva(uint value) => new(value);
    public static explicit operator AuxiliaryDataTableRva(int value) => new(unchecked((uint)value));
    public static explicit operator uint(AuxiliaryDataTableRva value) => value.Value;
    public static explicit operator int(AuxiliaryDataTableRva value) => unchecked((int)value.Value);
}

/// <summary>Image offset of an inliner list.</summary>
public readonly record struct InlinerListOffset(uint Value)
{
    public static explicit operator InlinerListOffset(uint value) => new(value);
    public static explicit operator InlinerListOffset(int value) => new(unchecked((uint)value));
    public static explicit operator uint(InlinerListOffset value) => value.Value;
    public static explicit operator int(InlinerListOffset value) => unchecked((int)value.Value);
}

/// <summary>Image offset of an instance-method entry payload.</summary>
public readonly record struct InstanceMethodPayloadOffset(uint Value)
{
    public static explicit operator InstanceMethodPayloadOffset(uint value) => new(value);
    public static explicit operator InstanceMethodPayloadOffset(int value) => new(unchecked((uint)value));
    public static explicit operator uint(InstanceMethodPayloadOffset value) => value.Value;
    public static explicit operator int(InstanceMethodPayloadOffset value) => unchecked((int)value.Value);
}

/// <summary>Image offset of a PGO payload.</summary>
public readonly record struct PgoPayloadOffset(uint Value)
{
    public static explicit operator PgoPayloadOffset(uint value) => new(value);
    public static explicit operator PgoPayloadOffset(int value) => new(unchecked((uint)value));
    public static explicit operator uint(PgoPayloadOffset value) => value.Value;
    public static explicit operator int(PgoPayloadOffset value) => unchecked((int)value.Value);
}

/// <summary>Image offset of a PGO data blob.</summary>
public readonly record struct PgoDataBlobOffset(uint Value)
{
    public static explicit operator PgoDataBlobOffset(uint value) => new(value);
    public static explicit operator PgoDataBlobOffset(int value) => new(unchecked((uint)value));
    public static explicit operator uint(PgoDataBlobOffset value) => value.Value;
    public static explicit operator int(PgoDataBlobOffset value) => unchecked((int)value.Value);
}

/// <summary>Image offset of an opaque fixup payload.</summary>
public readonly record struct R2ROpaqueFixupPayloadOffset(uint Value)
{
    public static explicit operator R2ROpaqueFixupPayloadOffset(uint value) => new(value);
    public static explicit operator R2ROpaqueFixupPayloadOffset(int value) => new(unchecked((uint)value));
    public static explicit operator uint(R2ROpaqueFixupPayloadOffset value) => value.Value;
    public static explicit operator int(R2ROpaqueFixupPayloadOffset value) => unchecked((int)value.Value);
}

/// <summary>Image offset of an external type-map inner table.</summary>
public readonly record struct ExternalTypeMapInnerHandle(uint Value)
{
    public static explicit operator ExternalTypeMapInnerHandle(uint value) => new(value);
    public static explicit operator ExternalTypeMapInnerHandle(int value) => new(unchecked((uint)value));
    public static explicit operator uint(ExternalTypeMapInnerHandle value) => value.Value;
    public static explicit operator int(ExternalTypeMapInnerHandle value) => unchecked((int)value.Value);
}

/// <summary>Image offset of a proxy type-map inner table.</summary>
public readonly record struct ProxyTypeMapInnerHandle(uint Value)
{
    public static explicit operator ProxyTypeMapInnerHandle(uint value) => new(value);
    public static explicit operator ProxyTypeMapInnerHandle(int value) => new(unchecked((uint)value));
    public static explicit operator uint(ProxyTypeMapInnerHandle value) => value.Value;
    public static explicit operator int(ProxyTypeMapInnerHandle value) => unchecked((int)value.Value);
}

/// <summary>Zero-based index into the import sections table.</summary>
public readonly record struct ImportSectionIndex(uint Value)
{
    public static explicit operator ImportSectionIndex(uint value) => new(value);
    public static explicit operator ImportSectionIndex(int value) => new(unchecked((uint)value));
    public static explicit operator uint(ImportSectionIndex value) => value.Value;
    public static explicit operator int(ImportSectionIndex value) => unchecked((int)value.Value);
}

/// <summary>Zero-based index of an import cell within an import section.</summary>
public readonly record struct ImportCellIndex(uint Value)
{
    public static explicit operator ImportCellIndex(uint value) => new(value);
    public static explicit operator ImportCellIndex(int value) => new(unchecked((uint)value));
    public static explicit operator uint(ImportCellIndex value) => value.Value;
    public static explicit operator int(ImportCellIndex value) => unchecked((int)value.Value);
}
