// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

using Internal.ReadyToRunConstants;

namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Handle to the NativeHashtable stored in a CrossModuleInlineInfo section (section 119, v6.3+).
    /// Obtain one from <see cref="ReadyToRunReader.GetCrossModuleInlineInfoTable(ReadyToRunSection)"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All values are compressed unsigned integers (<c>uint*</c>). "Multi-module" means the image header
    /// has <c>READYTORUN_FLAG_MultiModuleVersionBubble</c>
    /// (<see cref="CrossModuleInlineInfoHashTableHandle.IsMultiModuleFormat"/>).
    /// </para>
    /// <code>
    /// CrossModuleInlineInfo section
    /// +-----------------------------------------------------------+
    /// | NativeHashtable                                           |
    /// |   key: version-resilient hash of the inlinee MethodDef    |
    /// |   each entry points at one inlinee record (below)         |
    /// +-----------------------------------------------------------+
    ///
    /// Inlinee record
    /// +-----------------------------------------------------------+
    /// | count     uint*  number of uint* values that follow       |
    /// +-----------------------------------------------------------+
    /// | inlinee   uint*  bits 2+ = index (see below)              |
    /// |                  bit 1   = has cross-module inliners      |
    /// |                  bit 0   = inlinee is cross-module        |
    /// |     bit 0 set:   index is an ILBody import cell index     |
    /// |     bit 0 clear: index is a MethodDef RID                 |
    /// | [module]  uint*  only if bit 0 clear AND multi-module     |
    /// +-----------------------------------------------------------+
    /// | only if bit 1 set:                                        |
    /// |   n       uint*  number of cross-module inliners          |
    /// |   n x     uint*  ILBody import cell index                 |
    /// +-----------------------------------------------------------+
    /// | local inliners, repeated until count values are read:     |
    /// |   multi-module:                                           |
    /// |     inliner  uint*  bits 1+ = RID delta, bit 0 = module   |
    /// |     [module] uint*  only if bit 0 was set                 |
    /// |   single-module:                                          |
    /// |     inliner  uint*  RID delta from previous inliner       |
    /// +-----------------------------------------------------------+
    /// </code>
    /// <para>
    /// A local inliner with no module index of its own uses the inlinee's module index.
    /// </para>
    /// <c>count</c> is the number of unsigned values that follow. A cross-module inlinee's index is a cell index
    /// in the import section flagged as ILBodyFixups; otherwise it is a MethodDef RID.
    /// Crossgen2 emitter: <c>InliningInfoNode (InfoType.CrossModuleInliningForCrossModuleDataOnly or InfoType.CrossModuleAllMethods)</c>.
    /// </remarks>
    public readonly struct CrossModuleInlineInfoHashTableHandle
    {
        internal int Offset { get; }

        internal int Size { get; }

        /// <summary>Whether the image has <c>READYTORUN_FLAG_MultiModuleVersionBubble</c>, which changes the entry encoding.</summary>
        public bool IsMultiModuleFormat { get; }

        internal CrossModuleInlineInfoHashTableHandle(int offset, int size, bool isMultiModuleFormat)
        {
            Offset = offset;
            Size = size;
            IsMultiModuleFormat = isMultiModuleFormat;
        }
    }

    public partial class ReadyToRunReader
    {
        /// <summary>
        /// Open the CrossModuleInlineInfo section as a NativeHashtable handle without decoding any entries.
        /// </summary>
        public CrossModuleInlineInfoHashTableHandle GetCrossModuleInlineInfoTable(ReadyToRunSection section)
        {
            int sectionOffset = ValidateAndGetNativeHashtableSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.CrossModuleInlineInfo,
                nameof(GetCrossModuleInlineInfoTable));
            bool multiModuleFormat = (ReadyToRunHeader.Flags & (uint)ReadyToRunFlags.READYTORUN_FLAG_MultiModuleVersionBubble) != 0;
            return new CrossModuleInlineInfoHashTableHandle(sectionOffset, section.Size, multiModuleFormat);
        }

        /// <summary>Lazily enumerate every entry in the CrossModuleInlineInfo hashtable.</summary>
        public IEnumerable<CrossModuleInlineEntryHandle> EnumerateCrossModuleInlineEntries(CrossModuleInlineInfoHashTableHandle table)
        {
            foreach (uint entry in EnumerateNativeHashtableEntries(table.Offset, table.Size, nameof(table)))
                yield return (CrossModuleInlineEntryHandle)entry;
        }

        /// <summary>
        /// Probe the bucket for <paramref name="versionResilientHash"/> and return the first entry that satisfies
        /// <paramref name="predicate"/>.
        /// </summary>
        public CrossModuleInlineEntryHandle? LookupCrossModuleInlineEntry(
            CrossModuleInlineInfoHashTableHandle table,
            int versionResilientHash,
            Func<CrossModuleInlineEntry, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            foreach (uint entry in LookupNativeHashtableEntries(table.Offset, table.Size, versionResilientHash, nameof(table)))
            {
                CrossModuleInlineEntryHandle handle = (CrossModuleInlineEntryHandle)entry;
                if (predicate(GetCrossModuleInlineEntry(table, handle)))
                    return handle;
            }

            return null;
        }

        /// <summary>Decode one CrossModuleInlineInfo entry and its inliner list.</summary>
        public CrossModuleInlineEntry GetCrossModuleInlineEntry(CrossModuleInlineInfoHashTableHandle table, CrossModuleInlineEntryHandle handle)
        {
            NativeParser curParser = OpenNativeHashtableEntry(table.Offset, table.Size, (uint)handle, nameof(table), out _);
            bool multiModuleFormat = table.IsMultiModuleFormat;

            uint streamSize = curParser.GetUnsigned();
            if (streamSize == 0)
                throw new BadImageFormatException("CrossModuleInlineInfo entry has an empty payload.");

            uint inlineeIndexAndFlags = curParser.GetUnsigned();
            streamSize--;

            uint inlineeIndex = inlineeIndexAndFlags >> 2;
            bool hasCrossModuleInliners = (inlineeIndexAndFlags & 0x2) != 0;
            bool crossModuleInlinee = (inlineeIndexAndFlags & 0x1) != 0;

            ModuleIndex? inlineeModuleIndex = null;
            if (!crossModuleInlinee && multiModuleFormat)
            {
                if (streamSize == 0)
                    throw new BadImageFormatException("CrossModuleInlineInfo inlinee module index is missing.");

                inlineeModuleIndex = (ModuleIndex)curParser.GetUnsigned();
                streamSize--;
            }

            var inliners = new List<CrossModuleInlinerRef>();

            if (hasCrossModuleInliners)
            {
                if (streamSize == 0)
                    throw new BadImageFormatException("CrossModuleInlineInfo cross-module inliner count is missing.");

                uint crossModuleInlinerCount = curParser.GetUnsigned();
                streamSize--;
                if (crossModuleInlinerCount > streamSize)
                    throw new BadImageFormatException("CrossModuleInlineInfo cross-module inliner count exceeds its payload.");

                for (uint i = 0; i < crossModuleInlinerCount; i++)
                {
                    uint inlinerIndex = curParser.GetUnsigned();
                    streamSize--;
                    inliners.Add(new CrossModuleInlinerRef(rid: null, ilBodyImportIndex: (ImportCellIndex)inlinerIndex, moduleIndex: null));
                }
            }

            uint currentRid = 0;
            while (streamSize > 0)
            {
                uint inlinerDeltaAndFlag = curParser.GetUnsigned();
                streamSize--;

                ModuleIndex? moduleIndex = inlineeModuleIndex;
                if (multiModuleFormat)
                {
                    uint inlinerDelta = inlinerDeltaAndFlag >> 1;
                    if (inlinerDelta > uint.MaxValue - currentRid)
                        throw new BadImageFormatException("CrossModuleInlineInfo inliner RID overflows UInt32.");

                    currentRid += inlinerDelta;
                    if ((inlinerDeltaAndFlag & 0x1) != 0)
                    {
                        if (streamSize == 0)
                            throw new BadImageFormatException("CrossModuleInlineInfo inliner module index is missing.");

                        moduleIndex = (ModuleIndex)curParser.GetUnsigned();
                        streamSize--;
                    }
                }
                else
                {
                    if (inlinerDeltaAndFlag > uint.MaxValue - currentRid)
                        throw new BadImageFormatException("CrossModuleInlineInfo inliner RID overflows UInt32.");

                    currentRid += inlinerDeltaAndFlag;
                }
                inliners.Add(new CrossModuleInlinerRef(rid: (MethodRid)currentRid, ilBodyImportIndex: null, moduleIndex: moduleIndex));
            }

            return crossModuleInlinee
                ? new CrossModuleInlineEntry(inlineeRid: null, inlineeILBodyImportIndex: (ImportCellIndex)inlineeIndex, inlineeModuleIndex, inliners)
                : new CrossModuleInlineEntry(inlineeRid: (MethodRid)inlineeIndex, inlineeILBodyImportIndex: null, inlineeModuleIndex, inliners);
        }
    }

    /// <summary>
    /// A single decoded entry in the CrossModuleInlineInfo table.
    /// Exactly one of <see cref="InlineeRid"/> and <see cref="InlineeILBodyImportIndex"/> is set.
    /// </summary>
    public sealed class CrossModuleInlineEntry
    {

        /// <summary>Whether the inlinee is a cross-module reference (ILBody import cell).</summary>
        public bool IsCrossModuleInlinee => InlineeILBodyImportIndex.HasValue;

        /// <summary>MethodDef RID of a local inlinee.</summary>
        public MethodRid? InlineeRid { get; }

        /// <summary>Cell index in the ILBodyFixups import section for a cross-module inlinee.</summary>
        public ImportCellIndex? InlineeILBodyImportIndex { get; }

        /// <summary>
        /// Module index of a local inlinee in the multi-module format; <see langword="null"/> otherwise
        /// (the owner module, or a cross-module inlinee).
        /// </summary>
        public ModuleIndex? InlineeModuleIndex { get; }

        /// <summary>List of inliner references. Cross-module inliners come first.</summary>
        public IReadOnlyList<CrossModuleInlinerRef> Inliners { get; }

        internal CrossModuleInlineEntry(
            MethodRid? inlineeRid,
            ImportCellIndex? inlineeILBodyImportIndex,
            ModuleIndex? inlineeModuleIndex,
            List<CrossModuleInlinerRef> inliners)
        {
            InlineeRid = inlineeRid;
            InlineeILBodyImportIndex = inlineeILBodyImportIndex;
            InlineeModuleIndex = inlineeModuleIndex;
            Inliners = inliners;
        }
    }

    /// <summary>
    /// A reference to an inliner method in the CrossModuleInlineInfo format.
    /// Exactly one of <see cref="Rid"/> and <see cref="ILBodyImportIndex"/> is set.
    /// </summary>
    public sealed class CrossModuleInlinerRef
    {
        /// <summary>Whether this is a cross-module reference (ILBody import cell).</summary>
        public bool IsCrossModule => ILBodyImportIndex.HasValue;

        /// <summary>MethodDef RID of a local inliner.</summary>
        public MethodRid? Rid { get; }

        /// <summary>Cell index in the ILBodyFixups import section for a cross-module inliner.</summary>
        public ImportCellIndex? ILBodyImportIndex { get; }

        /// <summary>
        /// Module index of a local inliner: its encoded override, otherwise the inlinee's module index.
        /// <see langword="null"/> means the owner module, or a cross-module inliner.
        /// </summary>
        public ModuleIndex? ModuleIndex { get; }

        internal CrossModuleInlinerRef(MethodRid? rid, ImportCellIndex? ilBodyImportIndex, ModuleIndex? moduleIndex)
        {
            Rid = rid;
            ILBodyImportIndex = ilBodyImportIndex;
            ModuleIndex = moduleIndex;
        }
    }
}
