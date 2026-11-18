// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Internal.Runtime;

using Xunit;

namespace System.Reflection.Metadata.ReadyToRun.Tests;

/// <summary>
/// Reflection-only tests that lock in the structural API conventions established during the
/// data-shape audit. They run without a ReadyToRun image, so they are fast and deterministic.
/// </summary>
public sealed class ApiConventionTests
{
    private static readonly Assembly StructuralAssembly = typeof(ReadyToRunReader).Assembly;

    private const string Namespace = "System.Reflection.Metadata.ReadyToRun";

    /// <summary>
    /// Table entry/payload types are decoded by the reader and must not be constructible by callers.
    /// (The R2R*FixupPayload signature-node family is intentionally excluded: those are value nodes
    /// with public constructors, not reader-owned table rows.)
    /// </summary>
    public static IEnumerable<object[]> ReaderOwnedEntryTypes() => new[]
    {
        new object[] { "AvailableTypeEntry" },
        new object[] { "ComponentAssemblyEntry" },
        new object[] { "CrossModuleInlineEntry" },
        new object[] { "DebugInfoEntry" },
        new object[] { "ExceptionInfoEntry" },
        new object[] { "GCRefMapEntry" },
        new object[] { "HotColdMapEntry" },
        new object[] { "ImportSectionEntry" },
        new object[] { "InliningInfo2Entry" },
        new object[] { "InliningInfoEntry" },
        new object[] { "InstanceMethodEntry" },
        new object[] { "MethodDefEntry" },
        new object[] { "RuntimeFunctionEntry" },
        new object[] { "PgoEntry" },
        new object[] { "ProfileDataInfoEntry" },
        new object[] { "ExternalTypeMapGroup" },
        new object[] { "ExternalTypeMapEntry" },
        new object[] { "ProxyTypeMapGroup" },
        new object[] { "ProxyTypeMapEntry" },
        new object[] { "TypeMapAssemblyTargetsEntry" },
        new object[] { "TypeMapNamedEntry" },
        new object[] { "WasmAsyncResumeInfoChunk" },
        new object[] { "ImportSignature" },
        new object[] { "DelayLoadMethodCallThunksSection" },
        new object[] { "ManifestMetadataSection" },
        new object[] { "AttributePresenceSection" },
    };

    [Theory]
    [MemberData(nameof(ReaderOwnedEntryTypes))]
    public void ReaderOwnedEntryTypes_HaveNoPublicConstructor(string typeName)
    {
        Type type = RequireType(typeName);
        ConstructorInfo[] publicCtors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicCtors);
    }

    /// <summary>
    /// Opaque handles are readonly value types with explicit 32-bit conversions.
    /// </summary>
    public static IEnumerable<object[]> OpaqueValueTypes() => new[]
    {
        new object[] { "ImageOffset" },
        new object[] { "ImageRVA" },
        new object[] { "PCode" },
        new object[] { "CodeRva" },
        new object[] { "MethodRid" },
        new object[] { "TypeRid" },
        new object[] { "RuntimeFunctionIndex" },
        new object[] { "EHInfoRva" },
        new object[] { "UnwindInfoRva" },
        new object[] { "DebugInfoOffset" },
        new object[] { "DelayLoadMethodThunkRva" },
        new object[] { "SignatureRva" },
        new object[] { "SignatureTableRva" },
        new object[] { "ImportSlotTableRva" },
        new object[] { "AuxiliaryDataTableRva" },
        new object[] { "InlinerListOffset" },
        new object[] { "InstanceMethodEntryHandle" },
        new object[] { "FixupCellListHandle" },
        new object[] { "ModuleIndex" },
        new object[] { "AvailableTypeEntryHandle" },
        new object[] { "InliningInfo2EntryHandle" },
        new object[] { "CrossModuleInlineEntryHandle" },
        new object[] { "ExternalTypeMapGroupHandle" },
        new object[] { "ProxyTypeMapGroupHandle" },
        new object[] { "TypeMapAssemblyTargetsEntryHandle" },
        new object[] { "PgoEntryHandle" },
        new object[] { "PgoDataBlobImageOffset" },
        new object[] { "PgoDataBlobBackReferenceDelta" },
        new object[] { "R2ROpaqueFixupPayloadOffset" },
        new object[] { "ExternalTypeMapInnerHandle" },
        new object[] { "ProxyTypeMapInnerHandle" },
        new object[] { "ImportSectionIndex" },
        new object[] { "ImportCellIndex" },
        new object[] { "FixupCellListImageOffset" },
    };

    [Theory]
    [MemberData(nameof(OpaqueValueTypes))]
    public void OpaqueValueTypes_AreReadonlyUIntBackedStructs(string typeName)
    {
        Type type = RequireType(typeName);
        Assert.True(type.IsValueType && !type.IsEnum, $"{typeName} should be a value type, not an enum.");
        Assert.True(type.IsDefined(typeof(IsReadOnlyAttribute), inherit: false), $"{typeName} should be readonly.");
        Assert.Equal(sizeof(uint), Marshal.SizeOf(type));
        Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(type.GetProperties(BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(type.GetMethod("Deconstruct"));

        MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
        MethodInfo fromUInt = Assert.Single(methods, method =>
            method.Name == "op_Explicit" && method.ReturnType == type &&
            method.GetParameters() is [{ ParameterType: var parameterType }] && parameterType == typeof(uint));
        MethodInfo fromInt = Assert.Single(methods, method =>
            method.Name == "op_Explicit" && method.ReturnType == type &&
            method.GetParameters() is [{ ParameterType: var parameterType }] && parameterType == typeof(int));
        MethodInfo toUInt = Assert.Single(methods, method =>
            method.Name == "op_Explicit" && method.ReturnType == typeof(uint) &&
            method.GetParameters() is [{ ParameterType: var parameterType }] && parameterType == type);
        MethodInfo toInt = Assert.Single(methods, method =>
            method.Name == "op_Explicit" && method.ReturnType == typeof(int) &&
            method.GetParameters() is [{ ParameterType: var parameterType }] && parameterType == type);
        Assert.DoesNotContain(methods, method => method.Name == "op_Implicit");

        object allBits = fromUInt.Invoke(null, new object[] { uint.MaxValue })!;
        Assert.Equal(uint.MaxValue, (uint)toUInt.Invoke(null, new[] { allBits })!);
        Assert.Equal(-1, (int)toInt.Invoke(null, new[] { allBits })!);
        Assert.NotEqual(Activator.CreateInstance(type), allBits);
        Assert.Equal(allBits, fromUInt.Invoke(null, new object[] { uint.MaxValue }));
        Assert.Equal(allBits, fromInt.Invoke(null, new object[] { -1 }));
        Assert.Equal($"{type.Name} {{ Value = {uint.MaxValue} }}", allBits.ToString());
    }

    [Fact]
    public void AvailableTypeEntry_ExposesTypedEntityHandle()
    {
        Type type = RequireType("AvailableTypeEntry");

        Assert.Equal(typeof(EntityHandle), type.GetProperty("Type", BindingFlags.Public | BindingFlags.Instance)?.PropertyType);
        Assert.Equal(typeof(bool), type.GetProperty("IsExportedType", BindingFlags.Public | BindingFlags.Instance)?.PropertyType);

        // Decoded values don't echo the handle they were read from, and the raw RID encoding
        // is not exposed separately from the typed handle.
        Assert.Null(type.GetProperty("Handle", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(type.GetProperty("Rid", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(type.GetMethod("GetEntityHandle", BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void HotColdMapEntry_IndicesAreRuntimeFunctionIndex()
    {
        Type type = RequireType("HotColdMapEntry");
        Type runtimeFunctionIndex = RequireType("RuntimeFunctionIndex");
        Assert.Equal(runtimeFunctionIndex, type.GetProperty("HotRuntimeFunctionIndex")?.PropertyType);
        Assert.Equal(runtimeFunctionIndex, type.GetProperty("ColdRuntimeFunctionIndex")?.PropertyType);
    }

    [Fact]
    public void ReaderPublicSurface_DoesNotExposePlatformReadersOrLegacyDecoders()
    {
        Assert.False(typeof(IPlatformBinaryReader).IsPublic);
        Assert.False(typeof(PEImageReader).IsPublic);
        Assert.Null(typeof(ReadyToRunReader).GetMethod("GetGcInfo"));
        Assert.Null(typeof(ReadyToRunReader).GetMethod("GetUnwindInfo"));
    }

    [Fact]
    public void ListBackedTable_UsesReaderOwnedCollection()
    {
        var source = new List<WasmAsyncResumeInfoChunk>
        {
            new(1, 2, 8),
        };
        var table = new WasmAsyncResumeInfoTable(source);

        Assert.Same(source, table.Chunks);
    }

    [Fact]
    public void ArrayBackedTable_UsesReaderOwnedCollection()
    {
        var source = new[] { (SignatureRva)123u };
        var table = new SignatureTable(source);

        Assert.Same(source, table.Entries);
    }

    [Fact]
    public void HeaderDirectories_UseReaderOwnedCollection()
    {
        var sections = new List<ReadyToRunSection>();
        var coreHeader = new ReadyToRunCoreHeader(0, sections);
        var header = new ReadyToRunHeader(
            ReadyToRunHeader.READYTORUN_SIGNATURE,
            ReadyToRunHeader.MAXIMUM_SUPPORTED_MAJOR_VERSION,
            ReadyToRunHeader.MAXIMUM_SUPPORTED_MINOR_VERSION,
            0,
            sections);

        Assert.Same(sections, coreHeader.Sections);
        Assert.Same(sections, header.Sections);
    }

    [Fact]
    public void TypeGenericInfoMap_ThrowsForOutOfRangeRids()
    {
        var table = new TypeGenericInfoMapTable(2, new byte[1]);

        Assert.Throws<ArgumentOutOfRangeException>(() => table.GetInfo((TypeRid)0));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.GetInfo((TypeRid)3));
    }

    [Fact]
    public void GCRefMapTable_ExposesEntriesAndIsNotEnumerable()
    {
        Type table = RequireType("GCRefMapTable");
        Type gcRefMap = RequireType("GCRefMap");

        PropertyInfo? entries = table.GetProperty("Entries", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(entries);
        Assert.Equal(typeof(IReadOnlyList<>).MakeGenericType(gcRefMap), entries!.PropertyType);

        // The table itself should not be an IEnumerable; iteration goes through Entries.
        Assert.DoesNotContain(
            table.GetInterfaces(),
            i => i == typeof(IEnumerable<>).MakeGenericType(gcRefMap));
    }

    [Fact]
    public void RuntimeFunctionEntry_HasNoArrayIndexAndUsesHandleTypes()
    {
        Type type = RequireType("RuntimeFunctionEntry");

        // The array index of a runtime function is not stored in the image.
        Assert.Null(type.GetProperty("Index", BindingFlags.Public | BindingFlags.Instance));

        Assert.Equal(RequireType("PCode"), type.GetProperty("StartPCode")?.PropertyType);
        Assert.Equal(typeof(Nullable<>).MakeGenericType(RequireType("CodeRva")), type.GetProperty("EndRva")?.PropertyType);
        Assert.Equal(RequireType("UnwindInfoRva"), type.GetProperty("UnwindRva")?.PropertyType);
    }

    [Fact]
    public void InstanceMethodEntryPointsTable_IsInertTokenWithReaderSideApis()
    {
        Type table = RequireType("InstanceMethodHashTableHandle");
        Assert.True(table.IsValueType);
        Assert.Empty(table.GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        // The table is a token: no eager Entries collection lives on it.
        Assert.Empty(table.GetProperties(BindingFlags.Public | BindingFlags.Instance));

        Type reader = typeof(ReadyToRunReader);
        MethodInfo? getTable = reader.GetMethod("GetInstanceMethodEntryPointsTable", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(getTable);
        Assert.Equal(table, getTable!.ReturnType);

        MethodInfo? enumerate = reader.GetMethod("EnumerateInstanceMethodEntries", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(enumerate);
        Assert.Equal(typeof(IEnumerable<>).MakeGenericType(RequireType("InstanceMethodHashTableEntry")), enumerate!.ReturnType);

        MethodInfo? lookup = reader.GetMethod(
            "LookupInstanceMethodEntry",
            new[]
            {
                table,
                typeof(int),
                typeof(Func<,>).MakeGenericType(RequireType("MethodSignature"), typeof(bool))
            });
        Assert.NotNull(lookup);
        Assert.Equal(typeof(Nullable<>).MakeGenericType(RequireType("InstanceMethodEntryHandle")), lookup!.ReturnType);
        ParameterInfo[] parameters = lookup.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal(table, parameters[0].ParameterType);
        Assert.Equal(typeof(int), parameters[1].ParameterType);
        Assert.Equal(typeof(Func<,>).MakeGenericType(RequireType("MethodSignature"), typeof(bool)), parameters[2].ParameterType);
    }

    [Fact]
    public void SignatureDecodingOptions_ArePublicAndImplementedByTheDefaultOptionsRecord()
    {
        Type optionsInterface = RequireType("IReadyToRunSignatureDecodingOptions");
        Type optionsType = RequireType("ReadyToRunSignatureDecodingOptions");

        Assert.True(optionsInterface.IsInterface);
        Assert.Contains(optionsType.GetInterfaces(), type => type == optionsInterface);

        Assert.Equal(typeof(ushort), optionsInterface.GetProperty("MajorVersion")?.PropertyType);
        Assert.Equal(typeof(ushort), optionsInterface.GetProperty("MinorVersion")?.PropertyType);
        Assert.Equal(typeof(ReadyToRunValidationMode), optionsInterface.GetProperty("ValidationMode")?.PropertyType);
    }

    [Fact]
    public void ReaderDecodingApis_HaveAdditiveOptionsOverloads()
    {
        Type optionsInterface = RequireType("IReadyToRunSignatureDecodingOptions");
        Type reader = typeof(ReadyToRunReader);
        Type instanceMethodEntry = RequireType("InstanceMethodEntryHandle");
        Type instanceMethodTable = RequireType("InstanceMethodHashTableHandle");
        Type pgoTable = RequireType("PgoHashTableHandle");
        Type pgoEntry = RequireType("PgoEntryHandle");

        Assert.NotNull(reader.GetMethod("DecodeFixupSignature", new[] { RequireType("SignatureRva"), optionsInterface }));
        Assert.NotNull(reader.GetMethod("GetInstanceMethodEntry", new[] { instanceMethodTable, instanceMethodEntry, optionsInterface }));
        Assert.NotNull(reader.GetMethod("GetPgoEntry", new[] { pgoTable, pgoEntry, optionsInterface }));
        Assert.NotNull(reader.GetMethod(
            "GetFixupCells",
            new[] { RequireType("ReadyToRunSection"), RequireType("FixupCellListHandle") }));
        Assert.NotNull(reader.GetMethod(
            "LookupInstanceMethodEntry",
            new[]
            {
                instanceMethodTable,
                typeof(int),
                typeof(Func<,>).MakeGenericType(RequireType("MethodSignature"), typeof(bool)),
                optionsInterface
            }));
    }

    [Fact]
    public void GcAndDebugModels_DoNotExposeTraversalDerivedValues()
    {
        Type safePointOffset = typeof(Amd64.GcInfo.SafePointOffset);
        Assert.Null(safePointOffset.GetProperty("Index", BindingFlags.Public | BindingFlags.Instance));

        Type boundsEntry = typeof(DebugInfoBoundsEntry);
        Assert.Null(boundsEntry.GetField("NativeOffset", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(boundsEntry.GetField("ILOffset", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(boundsEntry.GetField("NativeOffsetDelta", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(boundsEntry.GetField("ILOffsetDelta", BindingFlags.Public | BindingFlags.Instance));

        Type nativeVarInfo = typeof(NativeVarInfo);
        Assert.Null(nativeVarInfo.GetField("EndOffset", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(nativeVarInfo.GetField("Variable", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(nativeVarInfo.GetField("RangeLength", BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void HeaderDirectoryModels_HaveGetOnlyPropertiesAndAreReaderOwned()
    {
        Type header = typeof(ReadyToRunHeader);
        Assert.Empty(header.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.False(header.GetProperty(nameof(ReadyToRunHeader.MajorVersion))!.CanWrite);
        Assert.False(header.GetProperty(nameof(ReadyToRunHeader.MinorVersion))!.CanWrite);
        Assert.False(header.GetProperty(nameof(ReadyToRunHeader.Flags))!.CanWrite);
        Assert.False(header.GetProperty(nameof(ReadyToRunHeader.Sections))!.CanWrite);

        Type coreHeader = typeof(ReadyToRunCoreHeader);
        Assert.Empty(coreHeader.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.False(coreHeader.GetProperty(nameof(ReadyToRunCoreHeader.Flags))!.CanWrite);
        Assert.False(coreHeader.GetProperty(nameof(ReadyToRunCoreHeader.Sections))!.CanWrite);

        Type section = typeof(ReadyToRunSection);
        ConstructorInfo[] sectionConstructors = section.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Equal(2, sectionConstructors.Length);
        Assert.Contains(sectionConstructors, constructor =>
            constructor.GetParameters() is [{ ParameterType: var parameterType }] &&
            parameterType == typeof(KnownReadyToRunSection));
        Assert.Contains(sectionConstructors, constructor =>
            constructor.GetParameters() is [{ ParameterType: var parameterType }] &&
            parameterType == typeof(UnknownReadyToRunSection));
        Assert.False(section.GetProperty("Type")!.CanWrite);
        Assert.False(section.GetProperty(nameof(ReadyToRunSection.RelativeVirtualAddress))!.CanWrite);
        Assert.False(section.GetProperty(nameof(ReadyToRunSection.Size))!.CanWrite);
        Assert.Null(section.GetProperty("DelayLoadMethodThunkRva", BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void ReadyToRunSection_IsNonBoxingCSharpUnion()
    {
        Type section = typeof(ReadyToRunSection);

        Assert.True(section.IsDefined(typeof(UnionAttribute), inherit: false));
        Assert.Contains(typeof(IUnion), section.GetInterfaces());
        Assert.True(section.IsValueType);
        Assert.True(section.IsDefined(typeof(IsReadOnlyAttribute), inherit: false));
        Assert.Empty(section.GetFields(BindingFlags.Public | BindingFlags.Instance));
        Assert.Equal(typeof(bool), section.GetProperty(nameof(ReadyToRunSection.HasValue))?.PropertyType);
        Assert.Equal(typeof(object), section.GetProperty(nameof(ReadyToRunSection.Value))?.PropertyType);

        Assert.NotNull(section.GetMethod(
            nameof(ReadyToRunSection.TryGetValue),
            new[] { typeof(KnownReadyToRunSection).MakeByRefType() }));
        Assert.NotNull(section.GetMethod(
            nameof(ReadyToRunSection.TryGetValue),
            new[] { typeof(UnknownReadyToRunSection).MakeByRefType() }));
        Assert.NotNull(section.GetProperty(nameof(ReadyToRunSection.RawType)));
        Assert.Equal(typeof(uint), section.GetProperty(nameof(ReadyToRunSection.RawRelativeVirtualAddress))?.PropertyType);
        Assert.Equal(typeof(uint), section.GetProperty(nameof(ReadyToRunSection.RawSize))?.PropertyType);
    }

    [Fact]
    public void ReadyToRunSection_CasesPreserveTypedAndRawDirectoryFields()
    {
        var knownValue = new KnownReadyToRunSection(
            ReadyToRunSectionType.RuntimeFunctions,
            (ImageRVA)0x1234u,
            0x56);
        ReadyToRunSection known = knownValue;

        var knownMatch = known switch
        {
            KnownReadyToRunSection value => value,
            UnknownReadyToRunSection => throw new InvalidOperationException("Known section matched as unknown."),
        };
        Assert.Equal(knownValue, knownMatch);
        Assert.Equal((int)ReadyToRunSectionType.RuntimeFunctions, known.RawType);
        Assert.Equal(0x1234u, known.RawRelativeVirtualAddress);
        Assert.Equal(0x56u, known.RawSize);

        var unknownValue = new UnknownReadyToRunSection(999, (ImageRVA)0x2345u, 0x67);
        ReadyToRunSection unknown = unknownValue;

        var unknownMatch = unknown switch
        {
            KnownReadyToRunSection => throw new InvalidOperationException("Unknown section matched as known."),
            UnknownReadyToRunSection value => value,
        };
        Assert.Equal(unknownValue, unknownMatch);
        Assert.Equal(999, unknown.RawType);
        Assert.Equal(0x2345u, unknown.RawRelativeVirtualAddress);
        Assert.Equal(0x67u, unknown.RawSize);
        Assert.Equal(unknownValue, unknown.Value);

        Assert.True(default(ReadyToRunSection).HasValue);
        Assert.IsType<UnknownReadyToRunSection>(default(ReadyToRunSection).Value);

        ReadyToRunSection defaultKnown = default(KnownReadyToRunSection);
        Assert.Equal(
            default(KnownReadyToRunSection),
            defaultKnown switch
            {
                KnownReadyToRunSection value => value,
                UnknownReadyToRunSection => throw new InvalidOperationException("Default known case changed type."),
            });
    }

    [Fact]
    public void DelayLoadThunkDescriptor_ContainsOnlyDirectoryFields()
    {
        Type type = RequireType("DelayLoadMethodCallThunksSection");

        Assert.Equal(RequireType("ImageRVA"), type.GetProperty("SectionRva")?.PropertyType);
        Assert.Equal(typeof(int), type.GetProperty("Length")?.PropertyType);
        Assert.Null(type.GetProperty("FileOffset", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(type.GetProperty("Architecture", BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void TypeMapInnerLookup_RequiresOwningTable()
    {
        Type reader = typeof(ReadyToRunReader);
        Type externalHandle = RequireType("ExternalTypeMapInnerHandle");
        Type proxyHandle = RequireType("ProxyTypeMapInnerHandle");

        Assert.NotNull(reader.GetMethod("GetExternalTypeMapEntries", new[] { RequireType("ExternalTypeMapsHashTableHandle"), externalHandle }));
        Assert.Null(reader.GetMethod("GetExternalTypeMapEntries", new[] { externalHandle }));
        Assert.NotNull(reader.GetMethod("GetProxyTypeMapEntries", new[] { RequireType("ProxyTypeMapsHashTableHandle"), proxyHandle }));
        Assert.Null(reader.GetMethod("GetProxyTypeMapEntries", new[] { proxyHandle }));
    }

    private static Type RequireType(string simpleName)
    {
        Type? type = StructuralAssembly.GetType($"{Namespace}.{simpleName}", throwOnError: false);
        Assert.True(type is not null, $"Type {Namespace}.{simpleName} was not found in {StructuralAssembly.GetName().Name}.");
        return type!;
    }
}
