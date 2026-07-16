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
        private Dictionary<int, uint> _nativeArrayEndOffsets;

        public MethodDefEntryPointsTable GetMethodDefEntryPointsTable(ReadyToRunSection section)
        {
            int sectionOffset = ValidateAndGetSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.MethodDefEntryPoints,
                nameof(GetMethodDefEntryPointsTable));
            uint sectionEndOffset = checked((uint)(sectionOffset + section.Size));
            NativeArray methodEntryPoints = new NativeArray(_nativeReader, (uint)sectionOffset, sectionEndOffset);
            uint encodedCount = methodEntryPoints.GetCount();
            if (encodedCount > int.MaxValue)
                throw new BadImageFormatException("MethodDefEntryPoints count exceeds the supported range.");

            var entries = new NativeArrayHandle(sectionOffset, (int)encodedCount);
            _nativeArrayEndOffsets ??= new Dictionary<int, uint>();
            _nativeArrayEndOffsets[sectionOffset] = sectionEndOffset;

            return new MethodDefEntryPointsTable(entries);
        }

        public bool TryGetMethodDefEntryPoint(MethodDefEntryPointsTable table, MethodRid methodRid, out MethodDefEntry entry)
        {
            EnsureSemanticDecodingSupported(nameof(TryGetMethodDefEntryPoint));
            int rowId = (int)methodRid;
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

            entry = DecodeMethodDefEntryPoint(offset, table.Entries);
            return true;
        }

        public IEnumerable<(MethodRid MethodRid, MethodDefEntry Entry)> EnumerateMethodDefEntryPoints(MethodDefEntryPointsTable table)
        {
            EnsureSemanticDecodingSupported(nameof(EnumerateMethodDefEntryPoints));
            NativeArray methodEntryPoints = GetNativeArray(table.Entries);
            for (int rowId = 1; rowId <= table.EntryCount; rowId++)
            {
                int offset = 0;
                if (methodEntryPoints.TryGetAt((uint)(rowId - 1), ref offset))
                    yield return ((MethodRid)rowId, DecodeMethodDefEntryPoint(offset, table.Entries));
            }
        }

        private NativeArray GetNativeArray(NativeArrayHandle handle)
        {
            if (_nativeArrayEndOffsets is null
                || !_nativeArrayEndOffsets.TryGetValue(handle.Offset, out uint endOffset))
            {
                throw new ArgumentException(
                    "The NativeArray handle was not created by this ReadyToRunReader.",
                    nameof(handle));
            }

            return new NativeArray(_nativeReader, (uint)handle.Offset, endOffset);
        }

        private MethodDefEntry DecodeMethodDefEntryPoint(int offset, NativeArrayHandle handle)
        {
            uint endOffset = _nativeArrayEndOffsets[handle.Offset];
            RegisterPayloadRange(offset, handle.Offset, (int)endOffset);
            (RuntimeFunctionIndex runtimeFunctionIndex, FixupCellListHandle? fixupCellListHandle) =
                DecodeRuntimeFunctionIdAndFixupCellList(offset, handle.Offset, (int)endOffset);
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

        internal MethodDefEntry(RuntimeFunctionIndex entryPointIndex, FixupCellListHandle? fixupCellListHandle)
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

        internal FixupCellRef(uint tableIndex, uint cellIndex)
        {
            TableIndex = tableIndex;
            CellIndex = cellIndex;
        }
    }

    /// <summary>Opaque handle representing an index into the RuntimeFunctions table.</summary>
    public enum RuntimeFunctionIndex : uint {}
}
