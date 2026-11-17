// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;

namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Structural projection of the ExternalTypeMaps section (section 124).
    /// An outer NativeHashtable keyed by type-map group; each group may have an optional
    /// inner NativeHashtable mapping UTF-8 type names to result import/fixup references.
    /// </summary>
    /// <remarks>
    /// Crossgen2 emitter: <c>ExternalTypeMapObjectNode</c> / <c>ReadyToRunExternalTypeMapNode</c>.
    /// Runtime consumer: type-map resolution in the runtime's class loader.
    /// <para>
    /// All integers below are compressed unsigned values (<c>uint*</c>). A <c>string</c> is a
    /// <c>uint*</c> byte length followed by that many UTF-8 bytes. An import reference is two
    /// <c>uint*</c> values: import section index, then cell index.
    /// </para>
    /// <code>
    /// ExternalTypeMaps section
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
    /// |   |     key      string  (type name)                      |
    /// |   |     result   import reference (section, cell)         |
    /// |   +-------------------------------------------------------+
    /// |                                                           |
    /// | state 2 only: named entries follow the inner table        |
    /// |   +-------------------------------------------------------+
    /// |   | count    uint*                                        |
    /// |   | count x  { key string, target string }                |
    /// |   +-------------------------------------------------------+
    /// +-----------------------------------------------------------+
    /// </code>
    /// <para>
    /// Example: a group in state 1 with one mapping <c>"Foo" -&gt; (section 3, cell 4)</c>
    /// is stored as <c>[group ref] [1] [inner hashtable: { 3 'F' 'o' 'o' 3 4 }]</c>.
    /// </para>
    /// <para>
    /// <see cref="ExternalTypeMapGroup.InnerHandle"/> is the inner table's offset from the start of the
    /// section (which is also the start of the outer table).
    /// </para>
    /// </remarks>
    public readonly struct ExternalTypeMapsHashTableHandle
    {
        internal int Offset { get; }

        internal int Size { get; }

        internal ExternalTypeMapsHashTableHandle(int offset, int size)
        {
            Offset = offset;
            Size = size;
        }
    }

    /// <summary>
    /// A single outer entry in the ExternalTypeMaps NativeHashtable.
    /// Contains the group type reference, a validity flag, and (when valid)
    /// a handle to the inner NativeHashtable of name→type mappings.
    /// </summary>
    public sealed class ExternalTypeMapGroup
    {
        /// <summary>Structural reference to the group type (import section and fixup indices).</summary>
        public ImportFixupReference GroupRef { get; }

        /// <summary>How the runtime resolves this group.</summary>
        public TypeMapGroupState State { get; }

        /// <summary>
        /// Handle to the inner NativeHashtable, valid only when <see cref="State"/> is not <see cref="TypeMapGroupState.RuntimeAttributeFallback"/>.
        /// Resolve inner entries with <see cref="ReadyToRunReader.GetExternalTypeMapEntries"/>.
        /// </summary>
        public ExternalTypeMapInnerHandle InnerHandle { get; }

        internal ExternalTypeMapGroup(
            ImportFixupReference groupRef,
            TypeMapGroupState state,
            ExternalTypeMapInnerHandle innerHandle)
        {
            GroupRef = groupRef;
            State = state;
            InnerHandle = innerHandle;
        }
    }

    /// <summary>
    /// Opaque handle to the inner NativeHashtable of an ExternalTypeMaps outer entry.
    /// The underlying value is the absolute file offset of the inner hashtable header.
    /// </summary>
    /// <summary>
    /// A single entry in an ExternalTypeMaps inner NativeHashtable.
    /// Maps a UTF-8 string key (the fully-qualified external type name) to a result
    /// import/fixup reference.
    /// </summary>
    public sealed class ExternalTypeMapEntry
    {
        /// <summary>UTF-8 external type name key.</summary>
        public string Key { get; }

        /// <summary>Structural reference to the result type (import section and fixup indices).</summary>
        public ImportFixupReference ResultRef { get; }

        internal ExternalTypeMapEntry(string key, ImportFixupReference resultRef)
        {
            Key = key;
            ResultRef = resultRef;
        }
    }

    public partial class ReadyToRunReader
    {
        private static readonly UTF8Encoding s_strictUtf8 = new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

        /// <summary>
        /// Open the ExternalTypeMaps section as a NativeHashtable handle without decoding any groups.
        /// </summary>
        public ExternalTypeMapsHashTableHandle GetExternalTypeMapsTable(ReadyToRunSection section)
        {
            int sectionOffset = ValidateAndGetNativeHashtableSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.ExternalTypeMaps,
                nameof(GetExternalTypeMapsTable));
            return new ExternalTypeMapsHashTableHandle(sectionOffset, section.Size);
        }

        /// <summary>Lazily enumerate every group in the ExternalTypeMaps outer hashtable.</summary>
        public IEnumerable<ExternalTypeMapGroupHandle> EnumerateExternalTypeMapGroups(ExternalTypeMapsHashTableHandle table)
        {
            foreach (uint entry in EnumerateNativeHashtableEntries(table.Offset, table.Size, nameof(table)))
                yield return (ExternalTypeMapGroupHandle)entry;
        }

        /// <summary>
        /// Probe the bucket for <paramref name="versionResilientTypeHash"/> and return the first group that satisfies
        /// <paramref name="predicate"/>.
        /// </summary>
        public ExternalTypeMapGroupHandle? LookupExternalTypeMapGroup(
            ExternalTypeMapsHashTableHandle table,
            int versionResilientTypeHash,
            Func<ExternalTypeMapGroup, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            foreach (uint entry in LookupNativeHashtableEntries(table.Offset, table.Size, versionResilientTypeHash, nameof(table)))
            {
                ExternalTypeMapGroupHandle handle = (ExternalTypeMapGroupHandle)entry;
                if (predicate(GetExternalTypeMapGroup(table, handle)))
                    return handle;
            }

            return null;
        }

        /// <summary>Decode one ExternalTypeMaps group.</summary>
        public ExternalTypeMapGroup GetExternalTypeMapGroup(ExternalTypeMapsHashTableHandle table, ExternalTypeMapGroupHandle handle)
        {
            NativeParser curParser = OpenNativeHashtableEntry(table.Offset, table.Size, (uint)handle, nameof(table), out int tableEndOffset);
            uint groupSectionIdx = curParser.GetUnsigned();
            uint groupFixupIdx = curParser.GetUnsigned();
            TypeMapGroupState state = (TypeMapGroupState)curParser.GetUnsigned();
            ValidateTypeMapState(state);

            var groupRef = new ImportFixupReference((ImportSectionIndex)groupSectionIdx, (ImportCellIndex)groupFixupIdx);
            ExternalTypeMapInnerHandle innerHandle = default;

            if (state is TypeMapGroupState.PrecomputedFixups or TypeMapGroupState.PrecomputedFixupsAndTypeNames)
            {
                if (curParser.Offset >= (uint)tableEndOffset)
                    throw new BadImageFormatException("External type-map inner table starts outside its containing section.");
                innerHandle = (ExternalTypeMapInnerHandle)(curParser.Offset - (uint)table.Offset);
            }

            return new ExternalTypeMapGroup(groupRef, state, innerHandle);
        }

        /// <summary>
        /// Enumerates the inner entries of an ExternalTypeMaps group referenced by
        /// <paramref name="handle"/>. Each entry is a UTF-8 key to result-type reference mapping.
        /// </summary>
        /// <param name="table">The ExternalTypeMaps table that contains the inner table.</param>
        /// <param name="handle">The section-relative inner handle from <see cref="ExternalTypeMapGroup.InnerHandle"/>.</param>
        public IReadOnlyList<ExternalTypeMapEntry> GetExternalTypeMapEntries(
            ExternalTypeMapsHashTableHandle table,
            ExternalTypeMapInnerHandle handle)
        {
            EnsureSemanticDecodingSupported(nameof(GetExternalTypeMapEntries));
            if (handle == default)
                return Array.Empty<ExternalTypeMapEntry>();

            int sectionOffset = table.Offset;
            if (table.Size <= 0 || sectionOffset < 0 || (long)sectionOffset + table.Size > ImageEndOffset)
                throw new ArgumentException("The type-map table handle does not belong to this image.", nameof(table));
            int sectionEndOffset = sectionOffset + table.Size;
            if ((uint)handle >= (uint)table.Size)
                throw new BadImageFormatException("External type-map inner-table offset is outside its section.");

            uint innerOffset = checked((uint)(sectionOffset + (int)(uint)handle));
            NativeParser innerParser = new NativeParser(_nativeReader, innerOffset);
            NativeHashtable innerHashtable = new NativeHashtable(_nativeReader, innerParser, (uint)sectionEndOffset);
            NativeHashtable.AllEntriesEnumerator enumerator = innerHashtable.EnumerateAllEntries();

            var entries = new List<ExternalTypeMapEntry>();

            NativeParser curParser = enumerator.GetNext();
            while (!curParser.IsNull())
            {
                uint keyLength = curParser.GetUnsigned();
                if (keyLength > sectionEndOffset - (long)curParser.Offset)
                    throw new BadImageFormatException("External type-map key extends beyond its containing section.");

                byte[] keyBytes = new byte[(int)keyLength];
                int rawOffset = (int)curParser.Offset;
                _nativeReader.ReadSpanAt(ref rawOffset, keyBytes);
                curParser.Offset = (uint)rawOffset;

                string key;
                try
                {
                    key = s_strictUtf8.GetString(keyBytes);
                }
                catch (DecoderFallbackException exception)
                {
                    throw new BadImageFormatException("External type-map key contains invalid UTF-8.", exception);
                }

                uint resultSectionIdx = curParser.GetUnsigned();
                uint resultFixupIdx = curParser.GetUnsigned();

                entries.Add(new ExternalTypeMapEntry(
                    key,
                    new ImportFixupReference((ImportSectionIndex)resultSectionIdx, (ImportCellIndex)resultFixupIdx)));
                curParser = enumerator.GetNext();
            }

            return entries;
        }

        /// <summary>
        /// Reads the serialized name pairs appended after the inner hashtable of a version 28 or later type-map group.
        /// </summary>
        /// <param name="table">The ExternalTypeMaps table containing <paramref name="group"/>.</param>
        /// <param name="group">A group from <see cref="ReadyToRunReader.GetExternalTypeMapGroup"/>.</param>
        /// <returns>The named entries, or an empty list when <see cref="ExternalTypeMapGroup.State"/> is not <see cref="TypeMapGroupState.PrecomputedFixupsAndTypeNames"/>.</returns>
        public IReadOnlyList<TypeMapNamedEntry> GetExternalTypeMapNamedEntries(
            ExternalTypeMapsHashTableHandle table,
            ExternalTypeMapGroup group)
        {
            EnsureSemanticDecodingSupported(nameof(GetExternalTypeMapNamedEntries));
            ArgumentNullException.ThrowIfNull(group);
            int sectionOffset = table.Offset;
            if (table.Size <= 0 || sectionOffset < 0 || (long)sectionOffset + table.Size > ImageEndOffset)
                throw new ArgumentException("The type-map table handle does not belong to this image.", nameof(table));
            int sectionEndOffset = sectionOffset + table.Size;
            if ((uint)group.InnerHandle >= (uint)table.Size)
                throw new BadImageFormatException("External type-map inner-table offset is outside its section.");

            return group.State == TypeMapGroupState.PrecomputedFixupsAndTypeNames
                ? ReadTypeMapNamedEntries(
                    checked((uint)(sectionOffset + (int)(uint)group.InnerHandle)),
                    sectionEndOffset)
                : Array.Empty<TypeMapNamedEntry>();
        }
    }
}
