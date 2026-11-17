// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Structural projection of the ProxyTypeMaps section (section 125).
    /// An outer NativeHashtable keyed by type-map group; each group may have an optional
    /// inner NativeHashtable mapping key import/fixup references to result import/fixup references.
    /// </summary>
    /// <remarks>
    /// Crossgen2 emitter: <c>ReadyToRunProxyTypeMapNode</c>.
    /// The section format is the same outer shape as ExternalTypeMaps (section 124),
    /// but the inner entries use type references (import/fixup pairs) as keys instead of UTF-8 strings.
    /// <para>
    /// All integers below are compressed unsigned values (<c>uint*</c>). A <c>string</c> is a
    /// <c>uint*</c> byte length followed by that many UTF-8 bytes. An import reference is two
    /// <c>uint*</c> values: import section index, then cell index.
    /// </para>
    /// <code>
    /// ProxyTypeMaps section
    /// +-----------------------------------------------------------+
    /// | Outer NativeHashtable                                     |
    /// |   key: version-resilient hash of the group type           |
    /// |   each entry points at one group record (below)           |
    /// +-----------------------------------------------------------+
    ///
    /// Group record (one per outer entry)
    /// +-----------------------------------------------------------+
    /// | group type   import reference (section, cell)             |
    /// | state        uint*  (TypeMapGroupState)                   |
    /// +-----------------------------------------------------------+
    /// | state 0: record ends here, no inner table                 |
    /// |                                                           |
    /// | state 1 or 2: inner table starts at the next byte         |
    /// |   +-------------------------------------------------------+
    /// |   | Inner NativeHashtable          &lt;-- InnerHandle         |
    /// |   |   each entry:                                         |
    /// |   |     key      import reference (source type)           |
    /// |   |     value    import reference (proxy type)            |
    /// |   +-------------------------------------------------------+
    /// |                                                           |
    /// | state 2 only: named entries follow the inner table        |
    /// |   +-------------------------------------------------------+
    /// |   | count    uint*                                        |
    /// |   | count x  { source name string, proxy name string }    |
    /// |   +-------------------------------------------------------+
    /// +-----------------------------------------------------------+
    /// </code>
    /// <para>
    /// <see cref="ProxyTypeMapGroup.InnerHandle"/> is the inner table's offset from the start of the
    /// section (which is also the start of the outer table).
    /// </para>
    /// </remarks>
    public readonly struct ProxyTypeMapsHashTableHandle
    {
        internal int Offset { get; }

        internal int Size { get; }

        internal ProxyTypeMapsHashTableHandle(int offset, int size)
        {
            Offset = offset;
            Size = size;
        }
    }

    /// <summary>
    /// A single outer entry in the ProxyTypeMaps NativeHashtable.
    /// Contains the group type reference, a validity flag, and (when valid)
    /// a handle to the inner NativeHashtable of key→value type mappings.
    /// </summary>
    public sealed class ProxyTypeMapGroup
    {
        /// <summary>Structural reference to the group type (import section and fixup indices).</summary>
        public ImportFixupReference GroupRef { get; }

        /// <summary>How the runtime resolves this group.</summary>
        public TypeMapGroupState State { get; }

        /// <summary>
        /// Handle to the inner NativeHashtable, valid only when <see cref="State"/> is not <see cref="TypeMapGroupState.RuntimeAttributeFallback"/>.
        /// Resolve inner entries with <see cref="ReadyToRunReader.GetProxyTypeMapEntries"/>.
        /// </summary>
        public ProxyTypeMapInnerHandle InnerHandle { get; }

        internal ProxyTypeMapGroup(
            ImportFixupReference groupRef,
            TypeMapGroupState state,
            ProxyTypeMapInnerHandle innerHandle)
        {
            GroupRef = groupRef;
            State = state;
            InnerHandle = innerHandle;
        }
    }

    /// <summary>
    /// Opaque handle to the inner NativeHashtable of a ProxyTypeMaps outer entry.
    /// The underlying value is the absolute file offset of the inner hashtable header.
    /// </summary>
    /// <summary>
    /// A single entry in a ProxyTypeMaps inner NativeHashtable.
    /// Maps a key type import/fixup reference to a value type import/fixup reference.
    /// </summary>
    public sealed class ProxyTypeMapEntry
    {
        /// <summary>Structural reference to the key type (import section and fixup indices).</summary>
        public ImportFixupReference KeyRef { get; }

        /// <summary>Structural reference to the value/result type (import section and fixup indices).</summary>
        public ImportFixupReference ValueRef { get; }

        internal ProxyTypeMapEntry(ImportFixupReference keyRef, ImportFixupReference valueRef)
        {
            KeyRef = keyRef;
            ValueRef = valueRef;
        }
    }

    public partial class ReadyToRunReader
    {

        /// <summary>
        /// Open the ProxyTypeMaps section as a NativeHashtable handle without decoding any groups.
        /// </summary>
        public ProxyTypeMapsHashTableHandle GetProxyTypeMapsTable(ReadyToRunSection section)
        {
            int sectionOffset = ValidateAndGetNativeHashtableSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.ProxyTypeMaps,
                nameof(GetProxyTypeMapsTable));
            return new ProxyTypeMapsHashTableHandle(sectionOffset, section.Size);
        }

        /// <summary>Lazily enumerate every group in the ProxyTypeMaps outer hashtable.</summary>
        public IEnumerable<ProxyTypeMapGroupHandle> EnumerateProxyTypeMapGroups(ProxyTypeMapsHashTableHandle table)
        {
            foreach (uint entry in EnumerateNativeHashtableEntries(table.Offset, table.Size, nameof(table)))
                yield return (ProxyTypeMapGroupHandle)entry;
        }

        /// <summary>
        /// Probe the bucket for <paramref name="versionResilientTypeHash"/> and return the first group that satisfies
        /// <paramref name="predicate"/>.
        /// </summary>
        public ProxyTypeMapGroupHandle? LookupProxyTypeMapGroup(
            ProxyTypeMapsHashTableHandle table,
            int versionResilientTypeHash,
            Func<ProxyTypeMapGroup, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            foreach (uint entry in LookupNativeHashtableEntries(table.Offset, table.Size, versionResilientTypeHash, nameof(table)))
            {
                ProxyTypeMapGroupHandle handle = (ProxyTypeMapGroupHandle)entry;
                if (predicate(GetProxyTypeMapGroup(table, handle)))
                    return handle;
            }

            return null;
        }

        /// <summary>Decode one ProxyTypeMaps group.</summary>
        public ProxyTypeMapGroup GetProxyTypeMapGroup(ProxyTypeMapsHashTableHandle table, ProxyTypeMapGroupHandle handle)
        {
            NativeParser curParser = OpenNativeHashtableEntry(table.Offset, table.Size, (uint)handle, nameof(table), out int tableEndOffset);
            uint groupSectionIdx = curParser.GetUnsigned();
            uint groupFixupIdx = curParser.GetUnsigned();
            TypeMapGroupState state = (TypeMapGroupState)curParser.GetUnsigned();
            ValidateTypeMapState(state);

            var groupRef = new ImportFixupReference((ImportSectionIndex)groupSectionIdx, (ImportCellIndex)groupFixupIdx);
            ProxyTypeMapInnerHandle innerHandle = default;

            if (state is TypeMapGroupState.PrecomputedFixups or TypeMapGroupState.PrecomputedFixupsAndTypeNames)
            {
                if (curParser.Offset >= (uint)tableEndOffset)
                    throw new BadImageFormatException("Proxy type-map inner table starts outside its containing section.");
                innerHandle = (ProxyTypeMapInnerHandle)(curParser.Offset - (uint)table.Offset);
            }

            return new ProxyTypeMapGroup(groupRef, state, innerHandle);
        }

        /// <summary>
        /// Enumerates the inner entries of a ProxyTypeMaps group referenced by
        /// <paramref name="handle"/>. Each entry maps a key type reference to a value type reference.
        /// </summary>
        /// <param name="table">The ProxyTypeMaps table that contains the inner table.</param>
        /// <param name="handle">The section-relative inner handle from <see cref="ProxyTypeMapGroup.InnerHandle"/>.</param>
        public IReadOnlyList<ProxyTypeMapEntry> GetProxyTypeMapEntries(
            ProxyTypeMapsHashTableHandle table,
            ProxyTypeMapInnerHandle handle)
        {
            EnsureSemanticDecodingSupported(nameof(GetProxyTypeMapEntries));
            if (handle == default)
                return Array.Empty<ProxyTypeMapEntry>();

            int sectionOffset = table.Offset;
            if (table.Size <= 0 || sectionOffset < 0 || (long)sectionOffset + table.Size > ImageEndOffset)
                throw new ArgumentException("The type-map table handle does not belong to this image.", nameof(table));
            int sectionEndOffset = sectionOffset + table.Size;
            if ((uint)handle >= (uint)table.Size)
                throw new BadImageFormatException("Proxy type-map inner-table offset is outside its section.");

            uint innerOffset = checked((uint)(sectionOffset + (int)(uint)handle));
            NativeParser innerParser = new NativeParser(_nativeReader, innerOffset);
            NativeHashtable innerHashtable = new NativeHashtable(_nativeReader, innerParser, (uint)sectionEndOffset);
            NativeHashtable.AllEntriesEnumerator enumerator = innerHashtable.EnumerateAllEntries();

            var entries = new List<ProxyTypeMapEntry>();

            NativeParser curParser = enumerator.GetNext();
            while (!curParser.IsNull())
            {
                // Read key type reference: (importSectionIndex, fixupIndex).
                uint keySectionIdx = curParser.GetUnsigned();
                uint keyFixupIdx = curParser.GetUnsigned();

                // Read value type reference: (importSectionIndex, fixupIndex).
                uint valueSectionIdx = curParser.GetUnsigned();
                uint valueFixupIdx = curParser.GetUnsigned();

                entries.Add(new ProxyTypeMapEntry(
                    new ImportFixupReference((ImportSectionIndex)keySectionIdx, (ImportCellIndex)keyFixupIdx),
                    new ImportFixupReference((ImportSectionIndex)valueSectionIdx, (ImportCellIndex)valueFixupIdx)));

                curParser = enumerator.GetNext();
            }

            return entries;
        }

        /// <summary>
        /// Reads the serialized name pairs appended after the inner hashtable of a version 28 or later type-map group.
        /// </summary>
        /// <param name="table">The ProxyTypeMaps table containing <paramref name="group"/>.</param>
        /// <param name="group">A group from <see cref="ReadyToRunReader.GetProxyTypeMapGroup"/>.</param>
        /// <returns>The named entries, or an empty list when <see cref="ProxyTypeMapGroup.State"/> is not <see cref="TypeMapGroupState.PrecomputedFixupsAndTypeNames"/>.</returns>
        public IReadOnlyList<TypeMapNamedEntry> GetProxyTypeMapNamedEntries(
            ProxyTypeMapsHashTableHandle table,
            ProxyTypeMapGroup group)
        {
            EnsureSemanticDecodingSupported(nameof(GetProxyTypeMapNamedEntries));
            ArgumentNullException.ThrowIfNull(group);
            int sectionOffset = table.Offset;
            if (table.Size <= 0 || sectionOffset < 0 || (long)sectionOffset + table.Size > ImageEndOffset)
                throw new ArgumentException("The type-map table handle does not belong to this image.", nameof(table));
            int sectionEndOffset = sectionOffset + table.Size;
            if ((uint)group.InnerHandle >= (uint)table.Size)
                throw new BadImageFormatException("Proxy type-map inner-table offset is outside its section.");

            return group.State == TypeMapGroupState.PrecomputedFixupsAndTypeNames
                ? ReadTypeMapNamedEntries(
                    checked((uint)(sectionOffset + (int)(uint)group.InnerHandle)),
                    sectionEndOffset)
                : Array.Empty<TypeMapNamedEntry>();
        }
    }
}
