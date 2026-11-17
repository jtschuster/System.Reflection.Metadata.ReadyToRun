// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;


namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Structural projection of the DebugInfo section.
    /// A NativeSparseArray mapping runtime function IDs to debug data offsets.
    /// </summary>
    /// <remarks>
    /// <code>
    /// DebugInfo section:
    ///   NativeSparseArray: runtime-function index -> encoded NativeArray handle
    ///   encoded handle:   0 -> inline payload follows
    ///                     nonzero -> look back from the handle to shared payload
    /// </code>
    /// Crossgen2 emitter: <c>DebugInfoTableNode</c>.
    /// </remarks>
    public sealed class DebugInfoTable
    {
        /// <summary>
        /// NativeSparseArray whose slot <c>i</c> holds the encoded debug-info handle for RuntimeFunctions index <c>i</c>.
        /// </summary>
        public NativeArrayHandle Entries { get; }

        /// <summary>Number of RuntimeFunctions index slots represented by the underlying NativeArray.</summary>
        public int EntryCount => Entries.Count;

        internal int SectionOffset => Entries.Offset;

        internal int SectionSize { get; }

        internal DebugInfoTable(NativeArrayHandle entries, int sectionSize)
        {
            Entries = entries;
            SectionSize = sectionSize;
        }
    }

    public partial class ReadyToRunReader
    {
        /// <summary>
        /// Open the DebugInfo section's NativeSparseArray without decoding any entries.
        /// </summary>
        public DebugInfoTable GetDebugInfoTable(ReadyToRunSection section)
        {
            int sectionOffset = ValidateAndGetSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.DebugInfo,
                nameof(GetDebugInfoTable));
            NativeArray debugInfoArray = new NativeArray(
                _nativeReader,
                (uint)sectionOffset,
                checked((uint)(sectionOffset + section.Size)));
            uint count = debugInfoArray.GetCount();
            if (count > int.MaxValue)
                throw new BadImageFormatException("DebugInfo count exceeds the supported range.");

            return new DebugInfoTable(new NativeArrayHandle(sectionOffset, (int)count), section.Size);
        }

        /// <summary>
        /// Look up the debug-info handle for a RuntimeFunctions index.
        /// </summary>
        /// <returns><see langword="false"/> if the sparse array has no entry for <paramref name="index"/>.</returns>
        public bool TryGetDebugInfoOffset(DebugInfoTable table, RuntimeFunctionIndex index, out DebugInfoOffset offset)
        {
            ArgumentNullException.ThrowIfNull(table);
            if ((uint)index >= (uint)table.EntryCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            return TryGetDebugInfoOffsetCore(OpenDebugInfoArray(table), table, (uint)index, out offset);
        }

        /// <summary>
        /// Lazily enumerate every populated slot of the DebugInfo sparse array.
        /// </summary>
        public IEnumerable<DebugInfoEntry> EnumerateDebugInfoEntries(DebugInfoTable table)
        {
            ArgumentNullException.ThrowIfNull(table);
            return Enumerate(OpenDebugInfoArray(table), table);

            IEnumerable<DebugInfoEntry> Enumerate(NativeArray array, DebugInfoTable table)
            {
                for (uint i = 0; i < (uint)table.EntryCount; i++)
                {
                    if (TryGetDebugInfoOffsetCore(array, table, i, out DebugInfoOffset offset))
                        yield return new DebugInfoEntry((RuntimeFunctionIndex)i, offset);
                }
            }
        }

        private NativeArray OpenDebugInfoArray(DebugInfoTable table)
            => new NativeArray(_nativeReader, (uint)table.SectionOffset, checked((uint)(table.SectionOffset + table.SectionSize)));

        private static bool TryGetDebugInfoOffsetCore(NativeArray array, DebugInfoTable table, uint index, out DebugInfoOffset offset)
        {
            int imageOffset = 0;
            if (!array.TryGetAt(index, ref imageOffset))
            {
                offset = default;
                return false;
            }

            if (imageOffset < table.SectionOffset || imageOffset >= table.SectionOffset + table.SectionSize)
                throw new BadImageFormatException("Debug info entry offset is outside its containing section.");
            offset = (DebugInfoOffset)(imageOffset - table.SectionOffset);
            return true;
        }
    }

    /// <summary>
    /// A single entry in the DebugInfo table containing a section-relative handle to debug data.
    /// </summary>
    /// <remarks>
    /// <code>
    /// runtime-function index | section-relative offset of encoded NativeArray handle
    /// </code>
    /// </remarks>
    public sealed class DebugInfoEntry
    {
        /// <summary>Index into the RuntimeFunctions table for this sparse-array slot.</summary>
        public RuntimeFunctionIndex RuntimeFunctionIndex { get; }

        /// <summary>
        /// Offset of the encoded debug-data handle, relative to the start of the DebugInfo section.
        /// </summary>
        public DebugInfoOffset DebugInfoOffset { get; }

        internal DebugInfoEntry(RuntimeFunctionIndex runtimeFunctionIndex, DebugInfoOffset debugInfoOffset)
        {
            RuntimeFunctionIndex = runtimeFunctionIndex;
            DebugInfoOffset = debugInfoOffset;
        }
    }

}
