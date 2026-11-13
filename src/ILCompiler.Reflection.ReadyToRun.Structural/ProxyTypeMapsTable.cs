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
    /// </remarks>
    public sealed class ProxyTypeMapsTable
    {
        /// <summary>All decoded outer entries from the section's NativeHashtable.</summary>
        public IReadOnlyList<ProxyTypeMapGroup> Groups { get; }

        internal ProxyTypeMapsTable(IReadOnlyList<ProxyTypeMapGroup> groups)
        {
            Groups = groups;
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

        /// <summary>
        /// 0 = runtime fallback; 1 = fixups only; 2 = fixups and named entries (R2R 28+).
        /// </summary>
        public uint State { get; }

        /// <summary>
        /// Handle to the inner NativeHashtable, valid only when <see cref="State"/> is non-zero.
        /// Resolve inner entries with <see cref="ReadyToRunReader.GetProxyTypeMapEntries"/>.
        /// </summary>
        public ProxyTypeMapInnerHandle InnerHandle { get; }

        internal ProxyTypeMapGroup(
            ImportFixupReference groupRef,
            uint state,
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
    public enum ProxyTypeMapInnerHandle : uint { }

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
        private Dictionary<ProxyTypeMapInnerHandle, (int StartOffset, int EndOffset, uint State)> _proxyTypeMapRanges;

        /// <summary>
        /// Parses the ProxyTypeMaps section as an outer NativeHashtable of type-map groups.
        /// </summary>
        public ProxyTypeMapsTable GetProxyTypeMapsTable(ReadyToRunSection section)
        {
            int sectionOffset = ValidateAndGetSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.ProxyTypeMaps,
                nameof(GetProxyTypeMapsTable));
            int sectionEndOffset = checked(sectionOffset + section.Size);

            NativeParser outerParser = new NativeParser(_nativeReader, (uint)sectionOffset);
            NativeHashtable outerHashtable = new NativeHashtable(_nativeReader, outerParser, (uint)sectionEndOffset);
            NativeHashtable.AllEntriesEnumerator enumerator = outerHashtable.EnumerateAllEntries();

            var groups = new List<ProxyTypeMapGroup>();

            NativeParser curParser = enumerator.GetNext();
            while (!curParser.IsNull())
            {
                uint groupSectionIdx = curParser.GetUnsigned();
                uint groupFixupIdx = curParser.GetUnsigned();
                uint state = curParser.GetUnsigned();
                ValidateTypeMapState(state);

                var groupRef = new ImportFixupReference(groupSectionIdx, groupFixupIdx);
                ProxyTypeMapInnerHandle innerHandle = default;

                if (state is 1 or 2)
                {
                    innerHandle = (ProxyTypeMapInnerHandle)curParser.Offset;
                    RegisterProxyTypeMapRange(innerHandle, sectionOffset, sectionEndOffset, state);
                }

                groups.Add(new ProxyTypeMapGroup(groupRef, state, innerHandle));
                curParser = enumerator.GetNext();
            }

            return new ProxyTypeMapsTable(groups);
        }

        /// <summary>
        /// Enumerates the inner entries of a ProxyTypeMaps group referenced by
        /// <paramref name="handle"/>. Each entry maps a key type reference to a value type reference.
        /// </summary>
        /// <param name="handle">The inner handle from <see cref="ProxyTypeMapGroup.InnerHandle"/>.</param>
        public IReadOnlyList<ProxyTypeMapEntry> GetProxyTypeMapEntries(
            ProxyTypeMapInnerHandle handle)
        {
            EnsureSemanticDecodingSupported(nameof(GetProxyTypeMapEntries));
            if (handle == default)
                return Array.Empty<ProxyTypeMapEntry>();

            (int _, int sectionEndOffset, uint _) = GetProxyTypeMapRange(handle);
            NativeParser innerParser = new NativeParser(_nativeReader, (uint)handle);
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
                    new ImportFixupReference(keySectionIdx, keyFixupIdx),
                    new ImportFixupReference(valueSectionIdx, valueFixupIdx)));

                curParser = enumerator.GetNext();
            }

            return entries;
        }

        /// <summary>Reads serialized source and proxy names appended to a version 28 or later type map.</summary>
        public IReadOnlyList<TypeMapNamedEntry> GetProxyTypeMapNamedEntries(ProxyTypeMapInnerHandle handle)
        {
            EnsureSemanticDecodingSupported(nameof(GetProxyTypeMapNamedEntries));
            if (handle == default)
                return Array.Empty<TypeMapNamedEntry>();
            var range = GetProxyTypeMapRange(handle);
            return range.State == 2 ? ReadTypeMapNamedEntries((uint)handle, range.EndOffset) : Array.Empty<TypeMapNamedEntry>();
        }

        private void RegisterProxyTypeMapRange(
            ProxyTypeMapInnerHandle handle,
            int sectionStartOffset,
            int sectionEndOffset,
            uint state)
        {
            uint rawOffset = (uint)handle;
            if (rawOffset < sectionStartOffset || rawOffset >= sectionEndOffset)
                throw new BadImageFormatException("Proxy type-map inner table starts outside its containing section.");

            _proxyTypeMapRanges ??= new Dictionary<ProxyTypeMapInnerHandle, (int, int, uint)>();
            _proxyTypeMapRanges[handle] = (sectionStartOffset, sectionEndOffset, state);
        }

        private (int StartOffset, int EndOffset, uint State) GetProxyTypeMapRange(ProxyTypeMapInnerHandle handle)
        {
            if (_proxyTypeMapRanges is null
                || !_proxyTypeMapRanges.TryGetValue(handle, out var range))
            {
                throw new ArgumentException(
                    "The proxy type-map handle was not created by this ReadyToRunReader.",
                    nameof(handle));
            }

            return range;
        }
    }
}
