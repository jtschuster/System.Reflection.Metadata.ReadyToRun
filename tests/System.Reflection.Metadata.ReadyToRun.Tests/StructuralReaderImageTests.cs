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
    public void ReadOnlyMemoryConstructor_ReadsCallerPEMemoryWithoutCopying()
    {
        byte[] image = File.ReadAllBytes(_image.AssemblyPath);
        const int Padding = 5;
        byte[] buffer = new byte[image.Length + Padding];
        image.CopyTo(buffer, Padding);

        using var reader = new ReadyToRunReader(buffer.AsMemory(Padding));
        ReadyToRunSection section = Section(reader, ReadyToRunSectionType.RuntimeFunctions);
        ImageOffset offset = reader.GetOffsetForRVA(section.RelativeVirtualAddress);
        byte original = reader.ReadByte(offset);

        buffer[Padding + (int)offset] = unchecked((byte)~original);

        Assert.Equal(unchecked((byte)~original), reader.ReadByte(offset));
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
        ReadyToRunSection section = Section(scope.Reader, ReadyToRunSectionType.MethodDefEntryPoints);
        MethodDefEntryPointsTable table = scope.Reader.GetMethodDefEntryPointsTable(section);

        int count = 0;
        foreach (var item in scope.Reader.EnumerateMethodDefEntryPoints(table))
        {
            MethodDefEntry entry = item.Entry;
            if (entry.FixupCellList is FixupCellListHandle fixupCellList)
            {
                Assert.True((uint)fixupCellList < (uint)section.Size);
                scope.Reader.GetFixupCells(section, fixupCellList);
            }
            count++;
        }
        Assert.True(count >= 1, "Expected at least one MethodDef entry point.");
    }

    [Fact]
    public void AvailableTypes_TypeHandle_ResolvesAgainstMetadata()
    {
        using ReaderScope scope = _image.OpenReader();
        AvailableTypesHashTableHandle table = scope.Reader.GetAvailableTypesTable(Section(scope.Reader, ReadyToRunSectionType.AvailableTypes));
        MetadataReader metadata = scope.PEReader.GetMetadataReader();
        List<AvailableTypeEntry> entries = scope.Reader.EnumerateAvailableTypeEntries(table)
            .Select(handle => scope.Reader.GetAvailableTypeEntry(table, handle))
            .ToList();

        Assert.NotEmpty(entries);

        int typeDefCount = 0;
        foreach (AvailableTypeEntry entry in entries)
        {
            EntityHandle handle = entry.Type;
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

    [Fact]
    public void DebugInfo_TryGetOffsetAgreesWithEnumeration()
    {
        using ReaderScope scope = _image.OpenReader();
        ReadyToRunSection section = Section(scope.Reader, ReadyToRunSectionType.DebugInfo);
        DebugInfoTable table = scope.Reader.GetDebugInfoTable(section);
        Dictionary<uint, DebugInfoOffset> enumerated = scope.Reader.EnumerateDebugInfoEntries(table)
            .ToDictionary(entry => (uint)entry.RuntimeFunctionIndex, entry => entry.DebugInfoOffset);

        Assert.NotEmpty(enumerated);
        for (uint i = 0; i < (uint)table.EntryCount; i++)
        {
            bool found = scope.Reader.TryGetDebugInfoOffset(table, (RuntimeFunctionIndex)i, out DebugInfoOffset offset);
            Assert.Equal(enumerated.ContainsKey(i), found);
            if (found)
            {
                Assert.Equal(enumerated[i], offset);
                Assert.True((uint)offset < (uint)section.Size);
                scope.Reader.GetDebugInfo(table, offset);
            }
        }

        Assert.Throws<ArgumentOutOfRangeException>(
            () => scope.Reader.TryGetDebugInfoOffset(table, (RuntimeFunctionIndex)table.EntryCount, out _));
    }

    [Fact]
    public void AvailableTypes_LookupByVersionResilientHash_FindsEachEntry()
    {
        using ReaderScope scope = _image.OpenReader();
        AvailableTypesHashTableHandle table = scope.Reader.GetAvailableTypesTable(Section(scope.Reader, ReadyToRunSectionType.AvailableTypes));
        MetadataReader metadata = scope.PEReader.GetMetadataReader();

        int checkedCount = 0;
        foreach (AvailableTypeEntryHandle handle in scope.Reader.EnumerateAvailableTypeEntries(table))
        {
            AvailableTypeEntry entry = scope.Reader.GetAvailableTypeEntry(table, handle);
            if (entry.IsExportedType)
                continue;

            TypeDefinition typeDef = metadata.GetTypeDefinition((TypeDefinitionHandle)entry.Type);
            if (!typeDef.GetDeclaringType().IsNil)
                continue;

            int hash = VersionResilientTypeHash(metadata.GetString(typeDef.Namespace), metadata.GetString(typeDef.Name));
            AvailableTypeEntryHandle? found = scope.Reader.LookupAvailableTypeEntry(table, hash, candidate => candidate.Type == entry.Type);
            Assert.Equal(handle, found);
            Assert.Null(scope.Reader.LookupAvailableTypeEntry(table, hash, _ => false));
            checkedCount++;
        }

        Assert.True(checkedCount >= 1, "Expected at least one top-level TypeDefinition-backed available type.");
    }

    // ─── Tier C: InstanceMethodEntryPoints enumerate + lookup ─────────────────

    [Fact]
    public void InstanceMethodEntries_EnumerationIsStableAndUnique()
    {
        using ReaderScope scope = _image.OpenReader();
        InstanceMethodHashTableHandle table = scope.Reader.GetInstanceMethodEntryPointsTable(InstanceMethodSection(scope.Reader));

        (InstanceMethodEntryHandle Handle, byte Low)[] first =
            scope.Reader.EnumerateInstanceMethodEntries(table).Select(e => (e.Handle, e.LowHashcode)).ToArray();
        (InstanceMethodEntryHandle Handle, byte Low)[] second =
            scope.Reader.EnumerateInstanceMethodEntries(table).Select(e => (e.Handle, e.LowHashcode)).ToArray();

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
        Assert.Equal(first.Length, first.Select(e => e.Handle).Distinct().Count());
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
        InstanceMethodHashTableHandle table = scope.Reader.GetInstanceMethodEntryPointsTable(section);

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
        InstanceMethodEntry? found = null;
        for (uint bucket = 0; bucket <= bucketMask; bucket++)
        {
            int hash = (int)((bucket << 8) | low);
            InstanceMethodEntryHandle? handle = scope.Reader.LookupInstanceMethodEntry(table, hash, sig => sig.ToString() == targetSig);
            if (handle is InstanceMethodEntryHandle foundHandle)
            {
                successes++;
                found = scope.Reader.GetInstanceMethodEntry(table, foundHandle);
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
        InstanceMethodHashTableHandle table = scope.Reader.GetInstanceMethodEntryPointsTable(section);

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
            Assert.Null(scope.Reader.LookupInstanceMethodEntry(table, hash, _ => true));
        }
    }

    [Fact]
    public void Lookup_FalsePredicate_ReturnsNull()
    {
        using ReaderScope scope = _image.OpenReader();
        ReadyToRunSection section = InstanceMethodSection(scope.Reader);
        InstanceMethodHashTableHandle table = scope.Reader.GetInstanceMethodEntryPointsTable(section);

        List<DecodedInstanceMethod> entries = DecodeAllInstanceMethods(scope.Reader);
        uint bucketMask = ReadBucketMask(scope.Reader, section);

        // Probe a bucket/low-byte combination that genuinely contains a candidate, then reject it
        // via the predicate: the result must still be null.
        byte low = entries[0].LowHashcode;
        for (uint bucket = 0; bucket <= bucketMask; bucket++)
        {
            int hash = (int)((bucket << 8) | low);
            Assert.Null(scope.Reader.LookupInstanceMethodEntry(table, hash, _ => false));
        }
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private sealed record DecodedInstanceMethod(
        InstanceMethodHashTableEntry Entry,
        byte LowHashcode,
        MethodSignature Signature,
        string SignatureString,
        int EntryPointIndex);

    private List<DecodedInstanceMethod> DecodeAllInstanceMethods(ReadyToRunReader reader)
    {
        ReadyToRunSection section = InstanceMethodSection(reader);
        InstanceMethodHashTableHandle table = reader.GetInstanceMethodEntryPointsTable(section);
        var result = new List<DecodedInstanceMethod>();
        foreach (InstanceMethodHashTableEntry entry in reader.EnumerateInstanceMethodEntries(table))
        {
            InstanceMethodEntry payload = reader.GetInstanceMethodEntry(table, entry.Handle);
            if (payload.FixupCellList is FixupCellListHandle fixupCellList)
            {
                Assert.True((uint)fixupCellList < (uint)section.Size);
                uint sectionImageOffset = (uint)reader.GetOffsetForRVA(section.RelativeVirtualAddress);
                uint fixupImageOffset = (uint)reader.GetFixupCellListImageOffset(section, fixupCellList);
                Assert.InRange(fixupImageOffset, sectionImageOffset, sectionImageOffset + (uint)section.Size - 1);
                reader.GetFixupCells(section, fixupCellList);
            }
            MethodSignature signature = MethodSignature.FromSignature(payload.MethodSignature);
            result.Add(new DecodedInstanceMethod(entry, entry.LowHashcode, signature, signature.ToString(), (int)payload.EntryPointIndex));
        }
        return result;
    }

    // Mirrors CoreCLR ComputeNameHashCode(namespace, name) from vm/typehashingalgorithms.h.
    private static int VersionResilientTypeHash(string @namespace, string name)
        => NameHash(System.Text.Encoding.UTF8.GetBytes(@namespace)) ^ NameHash(System.Text.Encoding.UTF8.GetBytes(name));

    private static int NameHash(byte[] src)
    {
        if (src.Length == 0)
            return 0;

        int hash1 = 0x6DA3B944;
        int hash2 = 0;
        for (int i = 0; i < src.Length; i += 2)
        {
            hash1 = unchecked(hash1 + RotateLeft(hash1, 5)) ^ (sbyte)src[i];
            if (i + 1 < src.Length)
                hash2 = unchecked(hash2 + RotateLeft(hash2, 5)) ^ (sbyte)src[i + 1];
        }

        hash1 = unchecked(hash1 + RotateLeft(hash1, 8));
        hash2 = unchecked(hash2 + RotateLeft(hash2, 8));
        return hash1 ^ hash2;

        static int RotateLeft(int value, int bitCount) => (int)System.Numerics.BitOperations.RotateLeft((uint)value, bitCount);
    }

    private static ReadyToRunSection InstanceMethodSection(ReadyToRunReader reader)
        => Section(reader, ReadyToRunSectionType.InstanceMethodEntryPoints);

    private static ReadyToRunSection Section(ReadyToRunReader reader, ReadyToRunSectionType type)
        => reader.GetSections().Single(s => s.Type == type);

    private static uint ReadBucketMask(ReadyToRunReader reader, ReadyToRunSection section)
    {
        int offset = (int)reader.GetOffsetForRVA(section.RelativeVirtualAddress);
        byte header = reader.ReadByte((ImageOffset)(uint)offset);
        int numberOfBucketsShift = header >> 2;
        return (uint)((1 << numberOfBucketsShift) - 1);
    }
}
