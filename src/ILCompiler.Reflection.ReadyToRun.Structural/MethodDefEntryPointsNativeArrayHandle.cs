// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;


namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Handle to the NativeArray stored in a MethodDefEntryPoints section.
    /// Obtain one from
    /// <see cref="ReadyToRunReader.GetMethodDefEntryPointsTable(ReadyToRunSection)"/>.
    /// </summary>
    /// <remarks>
    /// <code>
    /// NativeArray: MethodDef RID - 1 -> payload
    /// payload:     DecodeUnsigned(id) [optional fixup-list back-reference]
    /// </code>
    /// Crossgen2 emitter: <c>MethodEntryPointTableNode</c>.
    /// </remarks>
    public readonly struct MethodDefEntryPointsNativeArrayHandle
    {
        internal int Offset { get; }

        internal int SectionEndOffset { get; }

        /// <summary>
        /// Number of zero-based slots represented by the NativeArray.
        /// </summary>
        public int Count { get; }

        internal MethodDefEntryPointsNativeArrayHandle(int offset, int count, int sectionEndOffset)
        {
            Offset = offset;
            Count = count;
            SectionEndOffset = sectionEndOffset;
        }
    }

    public partial class ReadyToRunReader
    {
        /// <summary>
        /// Open the MethodDefEntryPoints section as a bounded NativeArray handle without decoding entries.
        /// </summary>
        public MethodDefEntryPointsNativeArrayHandle GetMethodDefEntryPointsTable(ReadyToRunSection section)
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

            return new MethodDefEntryPointsNativeArrayHandle(
                sectionOffset,
                (int)encodedCount,
                (int)sectionEndOffset);
        }

        public bool TryGetMethodDefEntryPoint(
            MethodDefEntryPointsNativeArrayHandle handle,
            MethodRid methodRid,
            out MethodDefEntry entry)
        {
            EnsureSemanticDecodingSupported(nameof(TryGetMethodDefEntryPoint));
            int rowId = (int)methodRid;
            if (rowId <= 0 || rowId > handle.Count)
                throw new ArgumentOutOfRangeException(nameof(methodRid));

            NativeArray methodEntryPoints = OpenMethodDefEntryPointsArray(handle);
            int offset = 0;
            if (!methodEntryPoints.TryGetAt((uint)(rowId - 1), ref offset))
            {
                entry = null;
                return false;
            }

            entry = DecodeMethodDefEntryPoint(offset, handle);
            return true;
        }

        public IEnumerable<(MethodRid MethodRid, MethodDefEntry Entry)> EnumerateMethodDefEntryPoints(
            MethodDefEntryPointsNativeArrayHandle handle)
        {
            EnsureSemanticDecodingSupported(nameof(EnumerateMethodDefEntryPoints));
            NativeArray methodEntryPoints = OpenMethodDefEntryPointsArray(handle);
            for (int rowId = 1; rowId <= handle.Count; rowId++)
            {
                int offset = 0;
                if (methodEntryPoints.TryGetAt((uint)(rowId - 1), ref offset))
                    yield return ((MethodRid)rowId, DecodeMethodDefEntryPoint(offset, handle));
            }
        }

        private NativeArray OpenMethodDefEntryPointsArray(MethodDefEntryPointsNativeArrayHandle handle)
            => new NativeArray(_nativeReader, (uint)handle.Offset, checked((uint)handle.SectionEndOffset));

        private MethodDefEntry DecodeMethodDefEntryPoint(int offset, MethodDefEntryPointsNativeArrayHandle handle)
        {
            (RuntimeFunctionIndex runtimeFunctionIndex, FixupCellListHandle? fixupCellListHandle) =
                DecodeRuntimeFunctionIdAndFixupCellList(
                    offset,
                    handle.Offset,
                    handle.SectionEndOffset,
                    out uint rawRuntimeFunctionIdAndFlags);
            return new MethodDefEntry(runtimeFunctionIndex, fixupCellListHandle, rawRuntimeFunctionIdAndFlags);
        }
    }

    /// <summary>
    /// One MethodDefEntryPoints payload containing the runtime function index and optional fixup-list handle.
    /// </summary>
    /// <remarks>
    /// <code>
    /// id bit 0 = has fixups; bit 1 = fixup list is back-referenced
    /// runtime-function index = id shifted right by 1 (or 2 when fixups are present)
    /// </code>
    /// </remarks>
    public sealed class MethodDefEntry
    {
        /// <summary>
        /// Raw runtime-function ID and flag value decoded from the compressed payload.
        /// </summary>
        public uint RawRuntimeFunctionIdAndFlags { get; }

        /// <summary>Index into the RuntimeFunctions array.</summary>
        public RuntimeFunctionIndex EntryPointIndex { get; }

        /// <summary>
        /// Handle to the encoded fixup-list reference, relative to the MethodDefEntryPoints section;
        /// resolve with <see cref="ReadyToRunReader.GetFixupCells"/>.
        /// </summary>
        public FixupCellListHandle? FixupCellList { get; }

        internal MethodDefEntry(
            RuntimeFunctionIndex entryPointIndex,
            FixupCellListHandle? fixupCellListHandle,
            uint rawRuntimeFunctionIdAndFlags)
        {
            EntryPointIndex = entryPointIndex;
            FixupCellList = fixupCellListHandle;
            RawRuntimeFunctionIdAndFlags = rawRuntimeFunctionIdAndFlags;
        }
    }

    /// <summary>
    /// A reference to a single fixup cell: identifies the import section and
    /// entry index within that section.
    /// </summary>
    public sealed class FixupCellRef
    {
        /// <summary>Index of the import section in the ImportSections array.</summary>
        public ImportSectionIndex TableIndex { get; }

        /// <summary>Index of the entry within the import section.</summary>
        public ImportCellIndex CellIndex { get; }

        internal FixupCellRef(ImportSectionIndex tableIndex, ImportCellIndex cellIndex)
        {
            TableIndex = tableIndex;
            CellIndex = cellIndex;
        }
    }

    /// <summary>Opaque handle representing an index into the RuntimeFunctions table.</summary>
}
