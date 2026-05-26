// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;


namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Structural projection of the MethodDefEntryPoints NativeArray section.
    /// Each entry maps a MethodDef RID to its RuntimeFunction index and the
    /// fixup cell references (import section table + cell index pairs) it needs
    /// resolved before execution.
    /// </summary>
    /// <remarks>
    /// Crossgen2 emitter: <c>MethodEntryPointTableNode</c>.
    /// </remarks>
    public sealed class MethodDefEntryPointsTable
    {
        /// <summary>
        /// NativeArray whose slot <c>rowId - 1</c> stores the MethodDef entry payload for MethodDef row ID <c>rowId</c>.
        /// </summary>
        public NativeArrayHandle Entries { get; }

        /// <summary>
        /// Number of MethodDef row ID slots represented by the underlying NativeArray.
        /// </summary>
        public int EntryCount => Entries.Count;

        internal MethodDefEntryPointsTable(NativeArrayHandle entries)
        {
            Entries = entries;
        }
    }

    public partial class ReadyToRunReader
    {
        public MethodDefEntryPointsTable GetMethodDefEntryPointsTable(ReadyToRunSection section)
        {
            int sectionOffset = GetOffsetForRVA(section.RelativeVirtualAddress);
            NativeArray methodEntryPoints = new NativeArray(_nativeReader, (uint)sectionOffset);
            var entries = new NativeArrayHandle(sectionOffset, checked((int)methodEntryPoints.GetCount()));

            return new MethodDefEntryPointsTable(entries);
        }

        public bool TryGetMethodDefEntryPoint(MethodDefEntryPointsTable table, int rowId, out MethodDefEntry entry)
        {
            if (rowId <= 0 || rowId > table.EntryCount)
            {
                entry = null;
                return false;
            }

            NativeArray methodEntryPoints = GetNativeArray(table.Entries);
            int offset = 0;
            if (!methodEntryPoints.TryGetAt((uint)(rowId - 1), ref offset))
            {
                entry = null;
                return false;
            }

            entry = DecodeMethodDefEntryPoint(offset);
            return true;
        }

        public IEnumerable<(int RowId, MethodDefEntry Entry)> EnumerateMethodDefEntryPoints(MethodDefEntryPointsTable table)
        {
            NativeArray methodEntryPoints = GetNativeArray(table.Entries);
            for (int rowId = 1; rowId <= table.EntryCount; rowId++)
            {
                int offset = 0;
                if (methodEntryPoints.TryGetAt((uint)(rowId - 1), ref offset))
                    yield return (rowId, DecodeMethodDefEntryPoint(offset));
            }
        }

        private NativeArray GetNativeArray(NativeArrayHandle handle)
        {
            return new NativeArray(_nativeReader, (uint)handle.Offset);
        }

        private MethodDefEntry DecodeMethodDefEntryPoint(int offset)
        {
            (RuntimeFunctionIndex runtimeFunctionIndex, FixupCellListHandle? fixupCellListHandle) = DecodeRuntimeFunctionIdAndFixupCellList(offset);
            return new MethodDefEntry(runtimeFunctionIndex, fixupCellListHandle);
        }
    }

    /// <summary>
    /// One MethodDefEntryPoints payload containing the runtime function index and optional fixup-list handle.
    /// </summary>
    public sealed class MethodDefEntry
    {
        /// <summary>Index into the RuntimeFunctions array.</summary>
        public RuntimeFunctionIndex EntryPointIndex { get; }

        /// <summary>Handle to fixup cells this method needs resolved before execution.</summary>
        public FixupCellListHandle? FixupCellListHandle { get; }

        public MethodDefEntry(RuntimeFunctionIndex entryPointIndex, FixupCellListHandle? fixupCellListHandle)
        {
            EntryPointIndex = entryPointIndex;
            FixupCellListHandle = fixupCellListHandle;
        }
    }

    /// <summary>
    /// A reference to a single fixup cell: identifies the import section and
    /// entry index within that section.
    /// </summary>
    public sealed class FixupCellRef
    {
        /// <summary>Index of the import section in the ImportSections array.</summary>
        public uint TableIndex { get; }

        /// <summary>Index of the entry within the import section.</summary>
        public uint CellIndex { get; }

        public FixupCellRef(uint tableIndex, uint cellIndex)
        {
            TableIndex = tableIndex;
            CellIndex = cellIndex;
        }
    }

    /// <summary>Opaque handle representing an index into the RuntimeFunctions table.</summary>
    public enum RuntimeFunctionIndex {}
}
