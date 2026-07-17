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
    /// Crossgen2 emitter: <c>TypeMapAssemblyTargetsNode</c>.
    /// Each outer entry is encoded as a tuple of:
    ///   (groupTypeRef: importSectionIdx, fixupIdx)
    ///   (modules: VertexSequence → count + N * (importSectionIdx, fixupIdx))
    /// </remarks>
    public sealed class TypeMapAssemblyTargetsTable
    {
        /// <summary>All decoded entries from the section's NativeHashtable.</summary>
        public IReadOnlyList<TypeMapAssemblyTargetsEntry> Entries { get; }

        internal TypeMapAssemblyTargetsTable(IReadOnlyList<TypeMapAssemblyTargetsEntry> entries)
        {
            Entries = entries;
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
        /// Parses the TypeMapAssemblyTargets section as a NativeHashtable of group-to-module-list mappings.
        /// </summary>
        public TypeMapAssemblyTargetsTable GetTypeMapAssemblyTargetsTable(ReadyToRunSection section)
        {
            int sectionOffset = ValidateAndGetSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.TypeMapAssemblyTargets,
                nameof(GetTypeMapAssemblyTargetsTable));
            int sectionEndOffset = checked(sectionOffset + section.Size);

            NativeParser parser = new NativeParser(_nativeReader, (uint)sectionOffset);
            NativeHashtable hashtable = new NativeHashtable(_nativeReader, parser, (uint)sectionEndOffset);
            NativeHashtable.AllEntriesEnumerator enumerator = hashtable.EnumerateAllEntries();

            var entries = new List<TypeMapAssemblyTargetsEntry>();

            NativeParser curParser = enumerator.GetNext();
            while (!curParser.IsNull())
            {
                // Read the group type reference: (importSectionIndex, fixupIndex).
                uint groupSectionIdx = curParser.GetUnsigned();
                uint groupFixupIdx = curParser.GetUnsigned();
                var groupRef = new ImportFixupReference(groupSectionIdx, groupFixupIdx);

                uint moduleCount = curParser.GetUnsigned();
                long remainingByteCount = sectionEndOffset - (long)curParser.Offset;
                if (moduleCount > remainingByteCount / 2)
                {
                    throw new BadImageFormatException(
                        "Type-map assembly target count exceeds its containing section.");
                }

                var moduleRefs = new List<ImportFixupReference>((int)moduleCount);

                for (uint i = 0; i < moduleCount; i++)
                {
                    uint moduleSectionIdx = curParser.GetUnsigned();
                    uint moduleFixupIdx = curParser.GetUnsigned();
                    moduleRefs.Add(new ImportFixupReference(moduleSectionIdx, moduleFixupIdx));
                }

                entries.Add(new TypeMapAssemblyTargetsEntry(groupRef, moduleRefs));
                curParser = enumerator.GetNext();
            }

            return new TypeMapAssemblyTargetsTable(entries);
        }
    }
}
