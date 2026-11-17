// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Reflection.Metadata.ReadyToRun;

/// <summary>File offset into the bytes of an image.</summary>
public readonly record struct ImageOffset
{
    private readonly uint _value;
    public ImageOffset(uint Value) => _value = Value;
    public static explicit operator ImageOffset(uint value) => new(value);
    public static explicit operator ImageOffset(int value) => new(unchecked((uint)value));
    public static explicit operator uint(ImageOffset value) => value._value;
    public static explicit operator int(ImageOffset value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(ImageOffset)} {{ Value = {_value} }}";
}

/// <summary>Relative virtual address (RVA) encoded in an image.</summary>
public readonly record struct ImageRVA
{
    private readonly uint _value;
    public ImageRVA(uint Value) => _value = Value;
    public static explicit operator ImageRVA(uint value) => new(value);
    public static explicit operator ImageRVA(int value) => new(unchecked((uint)value));
    public static explicit operator uint(ImageRVA value) => value._value;
    public static explicit operator int(ImageRVA value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(ImageRVA)} {{ Value = {_value} }}";
}

/// <summary>
/// Encoded code location from a ReadyToRun image. It is usually an RVA, but its interpretation is
/// target-dependent: ARM Thumb2 includes the Thumb bit, while WebAssembly uses a virtual
/// instruction-pointer index with bit 31 marking a funclet.
/// </summary>
public readonly record struct PCode
{
    private readonly uint _value;
    public PCode(uint Value) => _value = Value;
    public static explicit operator PCode(uint value) => new(value);
    public static explicit operator PCode(int value) => new(unchecked((uint)value));
    public static explicit operator uint(PCode value) => value._value;
    public static explicit operator int(PCode value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(PCode)} {{ Value = {_value} }}";
}

/// <summary>RVA of a code region.</summary>
public readonly record struct CodeRva
{
    private readonly uint _value;
    public CodeRva(uint Value) => _value = Value;
    public static explicit operator CodeRva(uint value) => new(value);
    public static explicit operator CodeRva(int value) => new(unchecked((uint)value));
    public static explicit operator uint(CodeRva value) => value._value;
    public static explicit operator int(CodeRva value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(CodeRva)} {{ Value = {_value} }}";
}

/// <summary>Method definition row identifier.</summary>
public readonly record struct MethodRid
{
    private readonly uint _value;
    public MethodRid(uint Value) => _value = Value;
    public static explicit operator MethodRid(uint value) => new(value);
    public static explicit operator MethodRid(int value) => new(unchecked((uint)value));
    public static explicit operator uint(MethodRid value) => value._value;
    public static explicit operator int(MethodRid value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(MethodRid)} {{ Value = {_value} }}";
}

/// <summary>Type definition row identifier.</summary>
public readonly record struct TypeRid
{
    private readonly uint _value;
    public TypeRid(uint Value) => _value = Value;
    public static explicit operator TypeRid(uint value) => new(value);
    public static explicit operator TypeRid(int value) => new(unchecked((uint)value));
    public static explicit operator uint(TypeRid value) => value._value;
    public static explicit operator int(TypeRid value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(TypeRid)} {{ Value = {_value} }}";
}

/// <summary>Zero-based index into the RuntimeFunctions table.</summary>
public readonly record struct RuntimeFunctionIndex
{
    private readonly uint _value;
    public RuntimeFunctionIndex(uint Value) => _value = Value;
    public static explicit operator RuntimeFunctionIndex(uint value) => new(value);
    public static explicit operator RuntimeFunctionIndex(int value) => new(unchecked((uint)value));
    public static explicit operator uint(RuntimeFunctionIndex value) => value._value;
    public static explicit operator int(RuntimeFunctionIndex value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(RuntimeFunctionIndex)} {{ Value = {_value} }}";
}

/// <summary>RVA of exception-handling information.</summary>
public readonly record struct EHInfoRva
{
    private readonly uint _value;
    public EHInfoRva(uint Value) => _value = Value;
    public static explicit operator EHInfoRva(uint value) => new(value);
    public static explicit operator EHInfoRva(int value) => new(unchecked((uint)value));
    public static explicit operator uint(EHInfoRva value) => value._value;
    public static explicit operator int(EHInfoRva value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(EHInfoRva)} {{ Value = {_value} }}";
}

/// <summary>RVA of unwind information.</summary>
public readonly record struct UnwindInfoRva
{
    private readonly uint _value;
    public UnwindInfoRva(uint Value) => _value = Value;
    public static explicit operator UnwindInfoRva(uint value) => new(value);
    public static explicit operator UnwindInfoRva(int value) => new(unchecked((uint)value));
    public static explicit operator uint(UnwindInfoRva value) => value._value;
    public static explicit operator int(UnwindInfoRva value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(UnwindInfoRva)} {{ Value = {_value} }}";
}

/// <summary>Offset of an encoded NativeArray debug-info handle, relative to its DebugInfo section.</summary>
public readonly record struct DebugInfoOffset
{
    private readonly uint _value;
    public DebugInfoOffset(uint Value) => _value = Value;
    public static explicit operator DebugInfoOffset(uint value) => new(value);
    public static explicit operator DebugInfoOffset(int value) => new(unchecked((uint)value));
    public static explicit operator uint(DebugInfoOffset value) => value._value;
    public static explicit operator int(DebugInfoOffset value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(DebugInfoOffset)} {{ Value = {_value} }}";
}

/// <summary>RVA of the delay-load method thunk section.</summary>
public readonly record struct DelayLoadMethodThunkRva
{
    private readonly uint _value;
    public DelayLoadMethodThunkRva(uint Value) => _value = Value;
    public static explicit operator DelayLoadMethodThunkRva(uint value) => new(value);
    public static explicit operator DelayLoadMethodThunkRva(int value) => new(unchecked((uint)value));
    public static explicit operator uint(DelayLoadMethodThunkRva value) => value._value;
    public static explicit operator int(DelayLoadMethodThunkRva value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(DelayLoadMethodThunkRva)} {{ Value = {_value} }}";
}

/// <summary>RVA of a ReadyToRun fixup signature.</summary>
public readonly record struct SignatureRva
{
    private readonly uint _value;
    public SignatureRva(uint Value) => _value = Value;
    public static explicit operator SignatureRva(uint value) => new(value);
    public static explicit operator SignatureRva(int value) => new(unchecked((uint)value));
    public static explicit operator uint(SignatureRva value) => value._value;
    public static explicit operator int(SignatureRva value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(SignatureRva)} {{ Value = {_value} }}";
}

/// <summary>RVA of an import signature table.</summary>
public readonly record struct SignatureTableRva
{
    private readonly uint _value;
    public SignatureTableRva(uint Value) => _value = Value;
    public static explicit operator SignatureTableRva(uint value) => new(value);
    public static explicit operator SignatureTableRva(int value) => new(unchecked((uint)value));
    public static explicit operator uint(SignatureTableRva value) => value._value;
    public static explicit operator int(SignatureTableRva value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(SignatureTableRva)} {{ Value = {_value} }}";
}

/// <summary>RVA of an import slot table.</summary>
public readonly record struct ImportSlotTableRva
{
    private readonly uint _value;
    public ImportSlotTableRva(uint Value) => _value = Value;
    public static explicit operator ImportSlotTableRva(uint value) => new(value);
    public static explicit operator ImportSlotTableRva(int value) => new(unchecked((uint)value));
    public static explicit operator uint(ImportSlotTableRva value) => value._value;
    public static explicit operator int(ImportSlotTableRva value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(ImportSlotTableRva)} {{ Value = {_value} }}";
}

/// <summary>RVA of an auxiliary import data table.</summary>
public readonly record struct AuxiliaryDataTableRva
{
    private readonly uint _value;
    public AuxiliaryDataTableRva(uint Value) => _value = Value;
    public static explicit operator AuxiliaryDataTableRva(uint value) => new(value);
    public static explicit operator AuxiliaryDataTableRva(int value) => new(unchecked((uint)value));
    public static explicit operator uint(AuxiliaryDataTableRva value) => value._value;
    public static explicit operator int(AuxiliaryDataTableRva value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(AuxiliaryDataTableRva)} {{ Value = {_value} }}";
}

/// <summary>Offset of an inliner list, relative to the start of the InliningInfo section.</summary>
public readonly record struct InlinerListOffset
{
    private readonly uint _value;
    public InlinerListOffset(uint Value) => _value = Value;
    public static explicit operator InlinerListOffset(uint value) => new(value);
    public static explicit operator InlinerListOffset(int value) => new(unchecked((uint)value));
    public static explicit operator uint(InlinerListOffset value) => value._value;
    public static explicit operator int(InlinerListOffset value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(InlinerListOffset)} {{ Value = {_value} }}";
}

/// <summary>Offset of an InstanceMethodEntryPoints hashtable entry (its method signature), relative to the start of the hashtable.</summary>
public readonly record struct InstanceMethodEntryHandle
{
    private readonly uint _value;
    public InstanceMethodEntryHandle(uint Value) => _value = Value;
    public static explicit operator InstanceMethodEntryHandle(uint value) => new(value);
    public static explicit operator InstanceMethodEntryHandle(int value) => new(unchecked((uint)value));
    public static explicit operator uint(InstanceMethodEntryHandle value) => value._value;
    public static explicit operator int(InstanceMethodEntryHandle value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(InstanceMethodEntryHandle)} {{ Value = {_value} }}";
}

/// <summary>
/// Handle to an encoded fixup-list reference: the offset of the entry's encoded ID, relative to the start
/// of its MethodDefEntryPoints or InstanceMethodEntryPoints section.
/// </summary>
/// <remarks>
/// <code>
/// encoded id (bit 0 = has fixups, bit 1 = back-reference)
/// [optional unsigned delta; list starts at delta bytes before this field]
/// </code>
/// </remarks>
public readonly record struct FixupCellListHandle
{
    private readonly uint _value;
    public FixupCellListHandle(uint Value) => _value = Value;
    public static explicit operator FixupCellListHandle(uint value) => new(value);
    public static explicit operator FixupCellListHandle(int value) => new(unchecked((uint)value));
    public static explicit operator uint(FixupCellListHandle value) => value._value;
    public static explicit operator int(FixupCellListHandle value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(FixupCellListHandle)} {{ Value = {_value} }}";
}

/// <summary>Image offset of resolved nibble-encoded fixup-list data.</summary>
public readonly record struct FixupCellListImageOffset
{
    private readonly uint _value;
    public FixupCellListImageOffset(uint Value) => _value = Value;
    public static explicit operator FixupCellListImageOffset(uint value) => new(value);
    public static explicit operator FixupCellListImageOffset(int value) => new(unchecked((uint)value));
    public static explicit operator uint(FixupCellListImageOffset value) => value._value;
    public static explicit operator int(FixupCellListImageOffset value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(FixupCellListImageOffset)} {{ Value = {_value} }}";
}

/// <summary>Offset of a PGO hashtable entry (its method signature), relative to the start of the PgoInstrumentationData hashtable.</summary>
public readonly record struct PgoEntryHandle
{
    private readonly uint _value;
    public PgoEntryHandle(uint Value) => _value = Value;
    public static explicit operator PgoEntryHandle(uint value) => new(value);
    public static explicit operator PgoEntryHandle(int value) => new(unchecked((uint)value));
    public static explicit operator uint(PgoEntryHandle value) => value._value;
    public static explicit operator int(PgoEntryHandle value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(PgoEntryHandle)} {{ Value = {_value} }}";
}

/// <summary>Image (file) offset of the start of a PGO data blob.</summary>
public readonly record struct PgoDataBlobImageOffset
{
    private readonly uint _value;
    public PgoDataBlobImageOffset(uint Value) => _value = Value;
    public static explicit operator PgoDataBlobImageOffset(uint value) => new(value);
    public static explicit operator PgoDataBlobImageOffset(int value) => new(unchecked((uint)value));
    public static explicit operator uint(PgoDataBlobImageOffset value) => value._value;
    public static explicit operator int(PgoDataBlobImageOffset value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(PgoDataBlobImageOffset)} {{ Value = {_value} }}";
}

/// <summary>Encoded backward-reference delta to a deduplicated PGO data blob.</summary>
public readonly record struct PgoDataBlobBackReferenceDelta
{
    private readonly uint _value;
    public PgoDataBlobBackReferenceDelta(uint Value) => _value = Value;
    public static explicit operator PgoDataBlobBackReferenceDelta(uint value) => new(value);
    public static explicit operator PgoDataBlobBackReferenceDelta(int value) => new(unchecked((uint)value));
    public static explicit operator uint(PgoDataBlobBackReferenceDelta value) => value._value;
    public static explicit operator int(PgoDataBlobBackReferenceDelta value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(PgoDataBlobBackReferenceDelta)} {{ Value = {_value} }}";
}

/// <summary>Image offset of an opaque fixup payload.</summary>
public readonly record struct R2ROpaqueFixupPayloadOffset
{
    private readonly uint _value;
    public R2ROpaqueFixupPayloadOffset(uint Value) => _value = Value;
    public static explicit operator R2ROpaqueFixupPayloadOffset(uint value) => new(value);
    public static explicit operator R2ROpaqueFixupPayloadOffset(int value) => new(unchecked((uint)value));
    public static explicit operator uint(R2ROpaqueFixupPayloadOffset value) => value._value;
    public static explicit operator int(R2ROpaqueFixupPayloadOffset value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(R2ROpaqueFixupPayloadOffset)} {{ Value = {_value} }}";
}

/// <summary>Offset of an external type-map inner table, relative to its section.</summary>
public readonly record struct ExternalTypeMapInnerHandle
{
    private readonly uint _value;
    public ExternalTypeMapInnerHandle(uint Value) => _value = Value;
    public static explicit operator ExternalTypeMapInnerHandle(uint value) => new(value);
    public static explicit operator ExternalTypeMapInnerHandle(int value) => new(unchecked((uint)value));
    public static explicit operator uint(ExternalTypeMapInnerHandle value) => value._value;
    public static explicit operator int(ExternalTypeMapInnerHandle value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(ExternalTypeMapInnerHandle)} {{ Value = {_value} }}";
}

/// <summary>Offset of a proxy type-map inner table, relative to its section.</summary>
public readonly record struct ProxyTypeMapInnerHandle
{
    private readonly uint _value;
    public ProxyTypeMapInnerHandle(uint Value) => _value = Value;
    public static explicit operator ProxyTypeMapInnerHandle(uint value) => new(value);
    public static explicit operator ProxyTypeMapInnerHandle(int value) => new(unchecked((uint)value));
    public static explicit operator uint(ProxyTypeMapInnerHandle value) => value._value;
    public static explicit operator int(ProxyTypeMapInnerHandle value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(ProxyTypeMapInnerHandle)} {{ Value = {_value} }}";
}

/// <summary>Zero-based index into the import sections table.</summary>
public readonly record struct ImportSectionIndex
{
    private readonly uint _value;
    public ImportSectionIndex(uint Value) => _value = Value;
    public static explicit operator ImportSectionIndex(uint value) => new(value);
    public static explicit operator ImportSectionIndex(int value) => new(unchecked((uint)value));
    public static explicit operator uint(ImportSectionIndex value) => value._value;
    public static explicit operator int(ImportSectionIndex value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(ImportSectionIndex)} {{ Value = {_value} }}";
}

/// <summary>Zero-based index of an import cell within an import section.</summary>
public readonly record struct ImportCellIndex
{
    private readonly uint _value;
    public ImportCellIndex(uint Value) => _value = Value;
    public static explicit operator ImportCellIndex(uint value) => new(value);
    public static explicit operator ImportCellIndex(int value) => new(unchecked((uint)value));
    public static explicit operator uint(ImportCellIndex value) => value._value;
    public static explicit operator int(ImportCellIndex value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(ImportCellIndex)} {{ Value = {_value} }}";
}

/// <summary>Index of a module in the image's manifest/component module list; resolved by the runtime with <c>Module::GetModuleFromIndexIfLoaded</c>. 0 is the owner module.</summary>
public readonly record struct ModuleIndex
{
    private readonly uint _value;
    public ModuleIndex(uint Value) => _value = Value;
    public static explicit operator ModuleIndex(uint value) => new(value);
    public static explicit operator ModuleIndex(int value) => new(unchecked((uint)value));
    public static explicit operator uint(ModuleIndex value) => value._value;
    public static explicit operator int(ModuleIndex value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(ModuleIndex)} {{ Value = {_value} }}";
}

/// <summary>Offset of a AvailableTypes hashtable entry, relative to the start of the hashtable (its section).</summary>
public readonly record struct AvailableTypeEntryHandle
{
    private readonly uint _value;
    public AvailableTypeEntryHandle(uint Value) => _value = Value;
    public static explicit operator AvailableTypeEntryHandle(uint value) => new(value);
    public static explicit operator AvailableTypeEntryHandle(int value) => new(unchecked((uint)value));
    public static explicit operator uint(AvailableTypeEntryHandle value) => value._value;
    public static explicit operator int(AvailableTypeEntryHandle value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(AvailableTypeEntryHandle)} {{ Value = {_value} }}";
}

/// <summary>Offset of a InliningInfo2 hashtable entry, relative to the start of the hashtable (its section).</summary>
public readonly record struct InliningInfo2EntryHandle
{
    private readonly uint _value;
    public InliningInfo2EntryHandle(uint Value) => _value = Value;
    public static explicit operator InliningInfo2EntryHandle(uint value) => new(value);
    public static explicit operator InliningInfo2EntryHandle(int value) => new(unchecked((uint)value));
    public static explicit operator uint(InliningInfo2EntryHandle value) => value._value;
    public static explicit operator int(InliningInfo2EntryHandle value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(InliningInfo2EntryHandle)} {{ Value = {_value} }}";
}

/// <summary>Offset of a CrossModuleInlineInfo hashtable entry, relative to the start of the hashtable (its section).</summary>
public readonly record struct CrossModuleInlineEntryHandle
{
    private readonly uint _value;
    public CrossModuleInlineEntryHandle(uint Value) => _value = Value;
    public static explicit operator CrossModuleInlineEntryHandle(uint value) => new(value);
    public static explicit operator CrossModuleInlineEntryHandle(int value) => new(unchecked((uint)value));
    public static explicit operator uint(CrossModuleInlineEntryHandle value) => value._value;
    public static explicit operator int(CrossModuleInlineEntryHandle value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(CrossModuleInlineEntryHandle)} {{ Value = {_value} }}";
}

/// <summary>Offset of a ExternalTypeMaps outer hashtable entry, relative to the start of the hashtable (its section).</summary>
public readonly record struct ExternalTypeMapGroupHandle
{
    private readonly uint _value;
    public ExternalTypeMapGroupHandle(uint Value) => _value = Value;
    public static explicit operator ExternalTypeMapGroupHandle(uint value) => new(value);
    public static explicit operator ExternalTypeMapGroupHandle(int value) => new(unchecked((uint)value));
    public static explicit operator uint(ExternalTypeMapGroupHandle value) => value._value;
    public static explicit operator int(ExternalTypeMapGroupHandle value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(ExternalTypeMapGroupHandle)} {{ Value = {_value} }}";
}

/// <summary>Offset of a ProxyTypeMaps outer hashtable entry, relative to the start of the hashtable (its section).</summary>
public readonly record struct ProxyTypeMapGroupHandle
{
    private readonly uint _value;
    public ProxyTypeMapGroupHandle(uint Value) => _value = Value;
    public static explicit operator ProxyTypeMapGroupHandle(uint value) => new(value);
    public static explicit operator ProxyTypeMapGroupHandle(int value) => new(unchecked((uint)value));
    public static explicit operator uint(ProxyTypeMapGroupHandle value) => value._value;
    public static explicit operator int(ProxyTypeMapGroupHandle value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(ProxyTypeMapGroupHandle)} {{ Value = {_value} }}";
}

/// <summary>Offset of a TypeMapAssemblyTargets hashtable entry, relative to the start of the hashtable (its section).</summary>
public readonly record struct TypeMapAssemblyTargetsEntryHandle
{
    private readonly uint _value;
    public TypeMapAssemblyTargetsEntryHandle(uint Value) => _value = Value;
    public static explicit operator TypeMapAssemblyTargetsEntryHandle(uint value) => new(value);
    public static explicit operator TypeMapAssemblyTargetsEntryHandle(int value) => new(unchecked((uint)value));
    public static explicit operator uint(TypeMapAssemblyTargetsEntryHandle value) => value._value;
    public static explicit operator int(TypeMapAssemblyTargetsEntryHandle value) => unchecked((int)value._value);
    public override string ToString() => $"{nameof(TypeMapAssemblyTargetsEntryHandle)} {{ Value = {_value} }}";
}
