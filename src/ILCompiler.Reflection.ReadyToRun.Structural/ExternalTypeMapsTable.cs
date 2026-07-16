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
    /// </remarks>
    public sealed class ExternalTypeMapsTable
    {
        /// <summary>All decoded outer entries from the section's NativeHashtable.</summary>
        public IReadOnlyList<ExternalTypeMapGroup> Groups { get; }

        internal ExternalTypeMapsTable(IReadOnlyList<ExternalTypeMapGroup> groups)
        {
            Groups = groups;
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

        /// <summary>
        /// 0 = invalid/throwing (inner hashtable absent); 1 = valid (inner hashtable present).
        /// </summary>
        public uint State { get; }

        /// <summary>
        /// Handle to the inner NativeHashtable, valid only when <see cref="State"/> is non-zero.
        /// Resolve inner entries with <see cref="ReadyToRunReader.GetExternalTypeMapEntries"/>.
        /// </summary>
        public ExternalTypeMapInnerHandle InnerHandle { get; }

        internal ExternalTypeMapGroup(
            ImportFixupReference groupRef,
            uint state,
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
    public enum ExternalTypeMapInnerHandle : uint { }

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
        /// <summary>
        /// Parses the ExternalTypeMaps section as an outer NativeHashtable of type-map groups.
        /// </summary>
        public ExternalTypeMapsTable GetExternalTypeMapsTable(ReadyToRunSection section)
        {
            int sectionOffset = GetOffsetForRVA(section.RelativeVirtualAddress);
            uint sectionEndOffset = (uint)(sectionOffset + section.Size);

            NativeParser outerParser = new NativeParser(_nativeReader, (uint)sectionOffset);
            NativeHashtable outerHashtable = new NativeHashtable(_nativeReader, outerParser, sectionEndOffset);
            NativeHashtable.AllEntriesEnumerator enumerator = outerHashtable.EnumerateAllEntries();

            var groups = new List<ExternalTypeMapGroup>();

            NativeParser curParser = enumerator.GetNext();
            while (!curParser.IsNull())
            {
                uint groupSectionIdx = curParser.GetUnsigned();
                uint groupFixupIdx = curParser.GetUnsigned();
                uint state = curParser.GetUnsigned();

                var groupRef = new ImportFixupReference(groupSectionIdx, groupFixupIdx);
                ExternalTypeMapInnerHandle innerHandle = default;

                if (state != 0)
                {
                    // The inner hashtable starts at the current parser position.
                    innerHandle = (ExternalTypeMapInnerHandle)curParser.Offset;
                }

                groups.Add(new ExternalTypeMapGroup(groupRef, state, innerHandle));
                curParser = enumerator.GetNext();
            }

            return new ExternalTypeMapsTable(groups);
        }

        /// <summary>
        /// Enumerates the inner entries of an ExternalTypeMaps group referenced by
        /// <paramref name="handle"/>. Each entry is a UTF-8 key to result-type reference mapping.
        /// </summary>
        /// <param name="handle">The inner handle from <see cref="ExternalTypeMapGroup.InnerHandle"/>.</param>
        /// <param name="outerSectionEndOffset">
        /// The file offset of the end of the outer section (used as the inner hashtable's end bound).
        /// </param>
        public IReadOnlyList<ExternalTypeMapEntry> GetExternalTypeMapEntries(
            ExternalTypeMapInnerHandle handle,
            int outerSectionEndOffset)
        {
            if (handle == default)
                return Array.Empty<ExternalTypeMapEntry>();

            NativeParser innerParser = new NativeParser(_nativeReader, (uint)handle);
            NativeHashtable innerHashtable = new NativeHashtable(_nativeReader, innerParser, (uint)outerSectionEndOffset);
            NativeHashtable.AllEntriesEnumerator enumerator = innerHashtable.EnumerateAllEntries();

            var entries = new List<ExternalTypeMapEntry>();

            NativeParser curParser = enumerator.GetNext();
            while (!curParser.IsNull())
            {
                // Read UTF-8 string key: NativeFormat encodes as length (unsigned) + raw bytes.
                uint keyLength = curParser.GetUnsigned();
                byte[] keyBytes = new byte[keyLength];
                int rawOffset = (int)curParser.Offset;
                _nativeReader.ReadSpanAt(ref rawOffset, keyBytes);
                curParser.Offset = (uint)rawOffset;

                string key = Encoding.UTF8.GetString(keyBytes);

                // Read the result type reference: (importSectionIndex, fixupIndex).
                uint resultSectionIdx = curParser.GetUnsigned();
                uint resultFixupIdx = curParser.GetUnsigned();

                entries.Add(new ExternalTypeMapEntry(key, new ImportFixupReference(resultSectionIdx, resultFixupIdx)));
                curParser = enumerator.GetNext();
            }

            return entries;
        }
    }
}
