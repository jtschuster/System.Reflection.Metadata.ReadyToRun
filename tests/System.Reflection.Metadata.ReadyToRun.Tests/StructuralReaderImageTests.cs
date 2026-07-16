// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;

using Internal.Runtime;

using Xunit;

namespace System.Reflection.Metadata.ReadyToRun.Tests;

/// <summary>
/// Structural-reader tests that run against a real, generics-rich ReadyToRun image (published once
/// by <see cref="GenericsR2RImageFixture"/>). Covers the general table getters (Tier B) and the
/// InstanceMethodEntryPoints enumerate/lookup APIs (Tier C).
/// </summary>
public sealed class StructuralReaderImageTests : IClassFixture<GenericsR2RImageFixture>
{
    private readonly GenericsR2RImageFixture _image;

    public StructuralReaderImageTests(GenericsR2RImageFixture image) => _image = image;

    // ─── Tier B: general table coverage ───────────────────────────────────────

    [Fact]
    public void RequiredSectionsArePresent()
    {
        using ReaderScope scope = _image.OpenReader();
        HashSet<ReadyToRunSectionType> types = scope.Reader.GetSections().Select(s => s.Type).ToHashSet();

        Assert.Contains(ReadyToRunSectionType.RuntimeFunctions, types);
        Assert.Contains(ReadyToRunSectionType.MethodDefEntryPoints, types);
        Assert.Contains(ReadyToRunSectionType.AvailableTypes, types);
        Assert.Contains(ReadyToRunSectionType.InstanceMethodEntryPoints, types);
    }

    [Fact]
    public void RuntimeFunctionsTable_HasEntries()
    {
        using ReaderScope scope = _image.OpenReader();
        RuntimeFunctionsTable table = scope.Reader.GetRuntimeFunctionsTable(Section(scope.Reader, ReadyToRunSectionType.RuntimeFunctions));

        Assert.NotEmpty(table.Entries);
        // The unwind RVA is a real 32-bit value; the cast must round-trip the raw bits.
        foreach (RuntimeFunctionEntry entry in table.Entries)
        {
            Assert.True((uint)entry.UnwindRva != 0);
        }
    }

    [Fact]
    public void MethodDefEntryPoints_Enumerate()
    {
        using ReaderScope scope = _image.OpenReader();
        MethodDefEntryPointsTable table = scope.Reader.GetMethodDefEntryPointsTable(Section(scope.Reader, ReadyToRunSectionType.MethodDefEntryPoints));

        int count = scope.Reader.EnumerateMethodDefEntryPoints(table).Count();
        Assert.True(count >= 1, "Expected at least one MethodDef entry point.");
    }

    [Fact]
    public void AvailableTypes_GetEntityHandle_ResolvesAgainstMetadata()
    {
        using ReaderScope scope = _image.OpenReader();
        AvailableTypesTable table = scope.Reader.GetAvailableTypesTable(Section(scope.Reader, ReadyToRunSectionType.AvailableTypes));
        MetadataReader metadata = scope.PEReader.GetMetadataReader();

        Assert.NotEmpty(table.Entries);

        int typeDefCount = 0;
        foreach (AvailableTypeEntry entry in table.Entries)
        {
            EntityHandle handle = entry.GetEntityHandle();
            Assert.False(handle.IsNil);

            if (entry.IsExportedType)
            {
                Assert.Equal(HandleKind.ExportedType, handle.Kind);
            }
            else
            {
                Assert.Equal(HandleKind.TypeDefinition, handle.Kind);
                // The handle must address a real row in this image's metadata.
                TypeDefinition typeDef = metadata.GetTypeDefinition((TypeDefinitionHandle)handle);
                Assert.False(typeDef.Name.IsNil);
                typeDefCount++;
            }
        }

        Assert.True(typeDefCount >= 1, "Expected at least one TypeDefinition-backed available type.");
    }

    // ─── Tier C: InstanceMethodEntryPoints enumerate + lookup ─────────────────

    [Fact]
    public void InstanceMethodEntries_EnumerationIsStableAndUnique()
    {
        using ReaderScope scope = _image.OpenReader();
        InstanceMethodEntryPointsTable table = scope.Reader.GetInstanceMethodEntryPointsTable(InstanceMethodSection(scope.Reader));

        (InstanceMethodPayloadOffset Offset, byte Low)[] first =
            scope.Reader.EnumerateInstanceMethodEntries(table).Select(e => (e.PayloadOffset, e.LowHashcode)).ToArray();
        (InstanceMethodPayloadOffset Offset, byte Low)[] second =
            scope.Reader.EnumerateInstanceMethodEntries(table).Select(e => (e.PayloadOffset, e.LowHashcode)).ToArray();

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
        Assert.Equal(first.Length, first.Select(e => e.Offset).Distinct().Count());
    }

    [Fact]
    public void InstanceMethodFixture_ExercisesGenericShapes()
    {
        using ReaderScope scope = _image.OpenReader();
        List<DecodedInstanceMethod> entries = DecodeAllInstanceMethods(scope.Reader);

        Assert.True(entries.Count >= 2, "Fixture should produce multiple instance method entries.");

        // At least one generic-method instantiation (Echo<T>) carries method type arguments.
        Assert.Contains(entries, e => !e.Signature.TypeArguments.IsEmpty);

        // At least one MethodDef RID appears under more than one distinct instantiation
        // (e.g. Box<int>.Get() and Box<long>.Get()), proving RID is not a unique key.
        bool ridReused = entries
            .GroupBy(e => e.Signature.Rid)
            .Any(g => g.Select(e => e.SignatureString).Distinct().Count() >= 2);
        Assert.True(ridReused, "Expected at least one MethodDef RID shared by distinct instantiations.");
    }

    [Fact]
    public void InstanceMethodPayloads_DecodeWithEntryPointInRange()
    {
        using ReaderScope scope = _image.OpenReader();
        int runtimeFunctionCount = scope.Reader.GetRuntimeFunctionsTable(Section(scope.Reader, ReadyToRunSectionType.RuntimeFunctions)).Entries.Count;

        List<DecodedInstanceMethod> entries = DecodeAllInstanceMethods(scope.Reader);
        Assert.NotEmpty(entries);

        foreach (DecodedInstanceMethod entry in entries)
        {
            int index = entry.EntryPointIndex;
            Assert.InRange(index, 0, runtimeFunctionCount - 1);
        }
    }

    [Fact]
    public void Lookup_FindsTargetOnlyInItsBucket()
    {
        using ReaderScope scope = _image.OpenReader();
        ReadyToRunSection section = InstanceMethodSection(scope.Reader);
        InstanceMethodEntryPointsTable table = scope.Reader.GetInstanceMethodEntryPointsTable(section);

        List<DecodedInstanceMethod> entries = DecodeAllInstanceMethods(scope.Reader);

        // Pick a target whose signature string is unique across the whole table, so the predicate
        // identifies exactly one entry.
        DecodedInstanceMethod target = entries
            .GroupBy(e => e.SignatureString)
            .Where(g => g.Count() == 1)
            .Select(g => g.Single())
            .First();

        uint bucketMask = ReadBucketMask(scope.Reader, section);
        // A single-bucket table would make "exactly one success" trivially true and prove nothing
        // about bucket selection. Require a multi-bucket table so the discrimination below is real.
        Assert.True(bucketMask >= 1, $"Expected a multi-bucket hashtable; bucketMask was {bucketMask}.");
        string targetSig = target.SignatureString;
        byte low = target.LowHashcode;

        int successes = 0;
        InstanceMethodPayload? found = null;
        for (uint bucket = 0; bucket <= bucketMask; bucket++)
        {
            int hash = (int)((bucket << 8) | low);
            InstanceMethodPayload? payload = scope.Reader.LookupInstanceMethodEntryPoint(table, hash, sig => sig.ToString() == targetSig);
            if (payload is not null)
            {
                successes++;
                found = payload;
            }
        }

        // The target lives in exactly one bucket; iterating every distinct bucket must find it once.
        // A lookup that ignored bucket selection would succeed for every bucket instead.
        Assert.Equal(1, successes);
        Assert.NotNull(found);
        Assert.Equal(targetSig, MethodSignature.FromSignature(found!.MethodSignature).ToString());
        Assert.Equal(target.EntryPointIndex, (int)found.EntryPointIndex);
    }

    [Fact]
    public void Lookup_AbsentLowByte_ReturnsNull()
    {
        using ReaderScope scope = _image.OpenReader();
        ReadyToRunSection section = InstanceMethodSection(scope.Reader);
        InstanceMethodEntryPointsTable table = scope.Reader.GetInstanceMethodEntryPointsTable(section);

        List<DecodedInstanceMethod> entries = DecodeAllInstanceMethods(scope.Reader);
        HashSet<byte> presentLowBytes = entries.Select(e => e.LowHashcode).ToHashSet();

        // With ~dozen entries there is always an unused low byte.
        int absent = Enumerable.Range(0, 256).First(b => !presentLowBytes.Contains((byte)b));

        uint bucketMask = ReadBucketMask(scope.Reader, section);
        for (uint bucket = 0; bucket <= bucketMask; bucket++)
        {
            int hash = (int)((bucket << 8) | (uint)absent);
            // Even a predicate that accepts everything must see no candidate: the low byte filters
            // out every entry before the predicate is consulted.
            Assert.Null(scope.Reader.LookupInstanceMethodEntryPoint(table, hash, _ => true));
        }
    }

    [Fact]
    public void Lookup_FalsePredicate_ReturnsNull()
    {
        using ReaderScope scope = _image.OpenReader();
        ReadyToRunSection section = InstanceMethodSection(scope.Reader);
        InstanceMethodEntryPointsTable table = scope.Reader.GetInstanceMethodEntryPointsTable(section);

        List<DecodedInstanceMethod> entries = DecodeAllInstanceMethods(scope.Reader);
        uint bucketMask = ReadBucketMask(scope.Reader, section);

        // Probe a bucket/low-byte combination that genuinely contains a candidate, then reject it
        // via the predicate: the result must still be null.
        byte low = entries[0].LowHashcode;
        for (uint bucket = 0; bucket <= bucketMask; bucket++)
        {
            int hash = (int)((bucket << 8) | low);
            Assert.Null(scope.Reader.LookupInstanceMethodEntryPoint(table, hash, _ => false));
        }
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private sealed record DecodedInstanceMethod(
        InstanceMethodEntry Entry,
        byte LowHashcode,
        MethodSignature Signature,
        string SignatureString,
        int EntryPointIndex);

    private List<DecodedInstanceMethod> DecodeAllInstanceMethods(ReadyToRunReader reader)
    {
        InstanceMethodEntryPointsTable table = reader.GetInstanceMethodEntryPointsTable(InstanceMethodSection(reader));
        var result = new List<DecodedInstanceMethod>();
        foreach (InstanceMethodEntry entry in reader.EnumerateInstanceMethodEntries(table))
        {
            InstanceMethodPayload payload = reader.GetInstanceMethodPayload(entry);
            MethodSignature signature = MethodSignature.FromSignature(payload.MethodSignature);
            result.Add(new DecodedInstanceMethod(entry, entry.LowHashcode, signature, signature.ToString(), (int)payload.EntryPointIndex));
        }
        return result;
    }

    private static ReadyToRunSection InstanceMethodSection(ReadyToRunReader reader)
        => Section(reader, ReadyToRunSectionType.InstanceMethodEntryPoints);

    private static ReadyToRunSection Section(ReadyToRunReader reader, ReadyToRunSectionType type)
        => reader.GetSections().Single(s => s.Type == type);

    private static uint ReadBucketMask(ReadyToRunReader reader, ReadyToRunSection section)
    {
        int offset = reader.GetOffsetForRVA(section.RelativeVirtualAddress);
        byte header = reader.ImageReader.ReadByte(ref offset);
        int numberOfBucketsShift = header >> 2;
        return (uint)((1 << numberOfBucketsShift) - 1);
    }
}
