// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Structural projection of the TypeMapAssemblyTargets section (section 126).
    /// A NativeHashtable keyed by group type; each entry lists the target modules
    /// that participate in that type map group.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All values are compressed unsigned integers (<c>uint*</c>). An import reference is two
    /// <c>uint*</c> values: import section index, then cell index.
    /// </para>
    /// <code>
    /// TypeMapAssemblyTargets section
    /// +-----------------------------------------------------------+
    /// | NativeHashtable                                           |
    /// |   key: version-resilient hash of the group type           |
    /// |   each entry points at one record (below)                 |
    /// +-----------------------------------------------------------+
    ///
    /// Record
    /// +-----------------------------------------------------------+
    /// | group type   import reference (section, cell)             |
    /// | n            uint*  number of target modules              |
    /// | n x          import reference (section, cell)             |
    /// +-----------------------------------------------------------+
    /// </code>
    /// Crossgen2 emitter: <c>TypeMapAssemblyTargetsNode</c>.
    /// </remarks>
    public readonly struct TypeMapAssemblyTargetsHashTableHandle
    {
        internal int Offset { get; }

        internal int Size { get; }

        internal TypeMapAssemblyTargetsHashTableHandle(int offset, int size)
        {
            Offset = offset;
            Size = size;
        }
    }

    /// <summary>
    /// A single entry in the TypeMapAssemblyTargets NativeHashtable.
    /// Contains a group type reference and the list of target module references
    /// that are co-located with that group in the assembly target map.
    /// </summary>
    public sealed class TypeMapAssemblyTargetsEntry
    {
        /// <summary>Structural reference to the group type (import section and fixup indices).</summary>
        public ImportFixupReference GroupRef { get; }

        /// <summary>
        /// Ordered list of structural references to the target modules
        /// (each is an import section and fixup index pair), in declaration order.
        /// Encoded as a VertexSequence (count followed by sequential entries).
        /// </summary>
        public IReadOnlyList<ImportFixupReference> ModuleRefs { get; }

        internal TypeMapAssemblyTargetsEntry(ImportFixupReference groupRef, IReadOnlyList<ImportFixupReference> moduleRefs)
        {
            GroupRef = groupRef;
            ModuleRefs = moduleRefs;
        }
    }

    public partial class ReadyToRunReader
    {
        /// <summary>
        /// Open the TypeMapAssemblyTargets section as a NativeHashtable handle without decoding any entries.
        /// </summary>
        public TypeMapAssemblyTargetsHashTableHandle GetTypeMapAssemblyTargetsTable(ReadyToRunSection section)
        {
            int sectionOffset = ValidateAndGetNativeHashtableSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.TypeMapAssemblyTargets,
                nameof(GetTypeMapAssemblyTargetsTable));
            return new TypeMapAssemblyTargetsHashTableHandle(sectionOffset, section.Size);
        }

        /// <summary>Lazily enumerate every entry in the TypeMapAssemblyTargets hashtable.</summary>
        public IEnumerable<TypeMapAssemblyTargetsEntryHandle> EnumerateTypeMapAssemblyTargetsEntries(TypeMapAssemblyTargetsHashTableHandle table)
        {
            foreach (uint entry in EnumerateNativeHashtableEntries(table.Offset, table.Size, nameof(table)))
                yield return (TypeMapAssemblyTargetsEntryHandle)entry;
        }

        /// <summary>
        /// Probe the bucket for <paramref name="versionResilientTypeHash"/> and return the first entry that satisfies
        /// <paramref name="predicate"/>.
        /// </summary>
        public TypeMapAssemblyTargetsEntryHandle? LookupTypeMapAssemblyTargetsEntry(
            TypeMapAssemblyTargetsHashTableHandle table,
            int versionResilientTypeHash,
            Func<TypeMapAssemblyTargetsEntry, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            foreach (uint entry in LookupNativeHashtableEntries(table.Offset, table.Size, versionResilientTypeHash, nameof(table)))
            {
                TypeMapAssemblyTargetsEntryHandle handle = (TypeMapAssemblyTargetsEntryHandle)entry;
                if (predicate(GetTypeMapAssemblyTargetsEntry(table, handle)))
                    return handle;
            }

            return null;
        }

        /// <summary>Decode one TypeMapAssemblyTargets entry.</summary>
        public TypeMapAssemblyTargetsEntry GetTypeMapAssemblyTargetsEntry(TypeMapAssemblyTargetsHashTableHandle table, TypeMapAssemblyTargetsEntryHandle handle)
        {
            NativeParser curParser = OpenNativeHashtableEntry(table.Offset, table.Size, (uint)handle, nameof(table), out int sectionEndOffset);
            uint groupSectionIdx = curParser.GetUnsigned();
            uint groupFixupIdx = curParser.GetUnsigned();
            var groupRef = new ImportFixupReference((ImportSectionIndex)groupSectionIdx, (ImportCellIndex)groupFixupIdx);

            uint moduleCount = curParser.GetUnsigned();
            long remainingByteCount = sectionEndOffset - (long)curParser.Offset;
            if (moduleCount > remainingByteCount / 2)
                throw new BadImageFormatException("Type-map assembly target count exceeds its containing section.");

            var moduleRefs = new List<ImportFixupReference>((int)moduleCount);
            for (uint i = 0; i < moduleCount; i++)
            {
                uint moduleSectionIdx = curParser.GetUnsigned();
                uint moduleFixupIdx = curParser.GetUnsigned();
                moduleRefs.Add(new ImportFixupReference((ImportSectionIndex)moduleSectionIdx, (ImportCellIndex)moduleFixupIdx));
            }

            return new TypeMapAssemblyTargetsEntry(groupRef, moduleRefs);
        }
    }
}
