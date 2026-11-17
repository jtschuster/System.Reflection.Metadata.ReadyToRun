// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Text;

namespace System.Reflection.Metadata.ReadyToRun;

/// <summary>How the runtime resolves a type-map group (runtime <c>TypeMapState</c>).</summary>
public enum TypeMapGroupState : uint
{
    /// <summary>No precomputed data; the runtime falls back to scanning type-map attributes.</summary>
    RuntimeAttributeFallback = 0,

    /// <summary>An inner hashtable of precomputed fixups follows the group.</summary>
    PrecomputedFixups = 1,

    /// <summary>Precomputed fixups followed by serialized type-name pairs (R2R 28+).</summary>
    PrecomputedFixupsAndTypeNames = 2,
}

/// <summary>A pair of strings in a type map's named-entry sequence.</summary>
public sealed class TypeMapNamedEntry
{
    public string Key { get; }
    public string Target { get; }

    internal TypeMapNamedEntry(string key, string target) => (Key, Target) = (key, target);
}

public partial class ReadyToRunReader
{
    private void ValidateTypeMapState(TypeMapGroupState state)
    {
        if (ValidationMode == ReadyToRunValidationMode.Strict &&
            (state > TypeMapGroupState.PrecomputedFixupsAndTypeNames ||
             (state == TypeMapGroupState.PrecomputedFixupsAndTypeNames && !FormatProfile.IsAtLeast(28, 0))))
            throw new NotSupportedException($"Type map state {(uint)state} is not supported in this ReadyToRun version.");
    }

    private IReadOnlyList<TypeMapNamedEntry> ReadTypeMapNamedEntries(uint handle, int sectionEndOffset)
    {
        var table = new NativeHashtable(_nativeReader, new NativeParser(_nativeReader, handle), (uint)sectionEndOffset);
        var parser = new NativeParser(_nativeReader, table.GetEndOffset(), (uint)sectionEndOffset);
        uint count = parser.GetUnsigned();
        if (count > (sectionEndOffset - (long)parser.Offset) / 2)
            throw new BadImageFormatException("Type map named-entry count exceeds its containing section.");

        var entries = new List<TypeMapNamedEntry>((int)count);
        for (uint i = 0; i < count; i++)
            entries.Add(new TypeMapNamedEntry(ReadTypeMapString(ref parser, sectionEndOffset), ReadTypeMapString(ref parser, sectionEndOffset)));
        return entries;
    }

    private string ReadTypeMapString(ref NativeParser parser, int endOffset)
    {
        uint length = parser.GetUnsigned();
        if (length > endOffset - (long)parser.Offset)
            throw new BadImageFormatException("Type map string extends beyond its containing section.");
        byte[] bytes = new byte[(int)length];
        int offset = (int)parser.Offset;
        _nativeReader.ReadSpanAt(ref offset, bytes);
        parser.Offset = (uint)offset;
        try
        {
            return s_strictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new BadImageFormatException("Type map string contains invalid UTF-8.", exception);
        }
    }
}
