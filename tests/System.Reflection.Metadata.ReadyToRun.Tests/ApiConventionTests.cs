// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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
        new object[] { "PgoEntry" },
        new object[] { "RuntimeFunctionEntry" },
        new object[] { "InstanceMethodPayload" },
        new object[] { "PgoPayload" },
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
    /// Opaque handles are enums whose underlying primitive matches the on-disk field size (uint for
    /// the 32-bit RVA/offset/index values), recoverable via cast, and never bitmask flags.
    /// </summary>
    public static IEnumerable<object[]> OpaqueHandleEnums() => new[]
    {
        new object[] { "ImageRVA" },
        new object[] { "PCode" },
        new object[] { "CodeRva" },
        new object[] { "MethodRid" },
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
        new object[] { "InstanceMethodPayloadOffset" },
        new object[] { "PgoPayloadOffset" },
        new object[] { "PgoDataBlobOffset" },
        new object[] { "R2ROpaqueFixupPayloadOffset" },
    };

    [Theory]
    [MemberData(nameof(OpaqueHandleEnums))]
    public void OpaqueHandleEnums_AreUIntBackedAndNotFlags(string typeName)
    {
        Type type = RequireType(typeName);
        Assert.True(type.IsEnum, $"{typeName} should be an enum.");
        Assert.Equal(typeof(uint), Enum.GetUnderlyingType(type));
        Assert.Empty(type.GetCustomAttributes(typeof(FlagsAttribute), inherit: false));

        // The raw value must be recoverable via a numeric cast.
        object zero = Enum.ToObject(type, 0u);
        Assert.Equal(0u, Convert.ToUInt32(zero));
    }

    [Fact]
    public void AvailableTypeEntry_ExposesRidIsExportedTypeAndGetEntityHandle()
    {
        Type type = RequireType("AvailableTypeEntry");

        PropertyInfo? rid = type.GetProperty("Rid", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(rid);
        Assert.Equal(typeof(uint), rid!.PropertyType);

        PropertyInfo? isExported = type.GetProperty("IsExportedType", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(isExported);
        Assert.Equal(typeof(bool), isExported!.PropertyType);

        MethodInfo? getHandle = type.GetMethod("GetEntityHandle", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(getHandle);
        Assert.Equal(typeof(EntityHandle), getHandle!.ReturnType);

        // The old SignatureBlobOffset-style raw exposure must be gone.
        Assert.Null(type.GetProperty("Type", BindingFlags.Public | BindingFlags.Instance));
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
        Type table = RequireType("InstanceMethodEntryPointsTable");
        Assert.True(table.IsSealed);
        Assert.Empty(table.GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        // The table is a token: no eager Entries collection lives on it.
        Assert.Null(table.GetProperty("Entries", BindingFlags.Public | BindingFlags.Instance));

        Type reader = typeof(ReadyToRunReader);
        MethodInfo? getTable = reader.GetMethod("GetInstanceMethodEntryPointsTable", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(getTable);
        Assert.Equal(table, getTable!.ReturnType);

        MethodInfo? enumerate = reader.GetMethod("EnumerateInstanceMethodEntries", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(enumerate);
        Assert.Equal(typeof(IEnumerable<>).MakeGenericType(RequireType("InstanceMethodEntry")), enumerate!.ReturnType);

        MethodInfo? lookup = reader.GetMethod(
            "LookupInstanceMethodEntryPoint",
            new[]
            {
                table,
                typeof(int),
                typeof(Func<,>).MakeGenericType(RequireType("MethodSignature"), typeof(bool))
            });
        Assert.NotNull(lookup);
        ParameterInfo[] parameters = lookup!.GetParameters();
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
        Type instanceMethodEntry = RequireType("InstanceMethodEntry");
        Type instanceMethodTable = RequireType("InstanceMethodEntryPointsTable");
        Type pgoEntry = RequireType("PgoEntry");

        Assert.NotNull(reader.GetMethod("DecodeFixupSignature", new[] { typeof(int), optionsInterface }));
        Assert.NotNull(reader.GetMethod("GetInstanceMethodPayload", new[] { instanceMethodEntry, optionsInterface }));
        Assert.NotNull(reader.GetMethod("GetPgoPayload", new[] { pgoEntry, optionsInterface }));
        Assert.NotNull(reader.GetMethod(
            "LookupInstanceMethodEntryPoint",
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
    public void HeaderDirectoryModels_AreImmutableAndReaderOwned()
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
        Assert.Empty(section.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.False(section.GetProperty(nameof(ReadyToRunSection.Type))!.CanWrite);
        Assert.False(section.GetProperty(nameof(ReadyToRunSection.RelativeVirtualAddress))!.CanWrite);
        Assert.False(section.GetProperty(nameof(ReadyToRunSection.Size))!.CanWrite);
        Assert.Null(section.GetProperty("DelayLoadMethodThunkRva", BindingFlags.Public | BindingFlags.Instance));
    }

    private static Type RequireType(string simpleName)
    {
        Type? type = StructuralAssembly.GetType($"{Namespace}.{simpleName}", throwOnError: false);
        Assert.True(type is not null, $"Type {Namespace}.{simpleName} was not found in {StructuralAssembly.GetName().Name}.");
        return type!;
    }
}
