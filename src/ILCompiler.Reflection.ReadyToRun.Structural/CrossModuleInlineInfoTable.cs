// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

using Internal.ReadyToRunConstants;


namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Structural projection of the CrossModuleInlineInfo section (section 119, v6.3+).
    /// A NativeHashtable of inlining entries with cross-module support.
    /// No method name resolution is performed.
    /// </summary>
    /// <remarks>
    /// Crossgen2 emitter: <c>InliningInfoNode (InfoType.CrossModuleInliningForCrossModuleDataOnly or InfoType.CrossModuleAllMethods)</c>.
    /// </remarks>
    public sealed class CrossModuleInlineInfoTable
    {
        public IReadOnlyList<CrossModuleInlineEntry> Entries { get; }

        internal CrossModuleInlineInfoTable(List<CrossModuleInlineEntry> entries)
        {
            Entries = entries;
        }
    }

    public partial class ReadyToRunReader
    {
        public CrossModuleInlineInfoTable GetCrossModuleInlineInfoTable(ReadyToRunSection section)
        {
            int sectionOffset = ValidateAndGetSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.CrossModuleInlineInfo,
                nameof(GetCrossModuleInlineInfoTable));
            bool multiModuleFormat = (ReadyToRunHeader.Flags & (uint)ReadyToRunFlags.READYTORUN_FLAG_MultiModuleVersionBubble) != 0;

            NativeParser parser = new NativeParser(_nativeReader, (uint)sectionOffset);
            NativeHashtable hashtable = new NativeHashtable(_nativeReader, parser, (uint)(sectionOffset + section.Size));
            var enumerator = hashtable.EnumerateAllEntries();
            var entries = new List<CrossModuleInlineEntry>();

            NativeParser curParser = enumerator.GetNext();
            while (!curParser.IsNull())
            {
                uint streamSize = curParser.GetUnsigned();
                if (streamSize == 0)
                    throw new BadImageFormatException("CrossModuleInlineInfo entry has an empty payload.");

                uint inlineeIndexAndFlags = curParser.GetUnsigned();
                streamSize--;

                uint inlineeIndex = inlineeIndexAndFlags >> 2;
                bool hasCrossModuleInliners = (inlineeIndexAndFlags & 0x2) != 0;
                bool crossModuleInlinee = (inlineeIndexAndFlags & 0x1) != 0;

                uint inlineeModuleIndex = 0;
                if (!crossModuleInlinee && multiModuleFormat)
                {
                    if (streamSize == 0)
                        throw new BadImageFormatException("CrossModuleInlineInfo inlinee module index is missing.");

                    inlineeModuleIndex = curParser.GetUnsigned();
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
                        inliners.Add(new CrossModuleInlinerRef(isCrossModule: true, index: inlinerIndex, moduleIndex: 0));
                    }
                }

                uint currentRid = 0;
                while (streamSize > 0)
                {
                    uint inlinerDeltaAndFlag = curParser.GetUnsigned();
                    streamSize--;

                    uint moduleIndex = inlineeModuleIndex;
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

                            moduleIndex = curParser.GetUnsigned();
                            streamSize--;
                        }
                    }
                    else
                    {
                        if (inlinerDeltaAndFlag > uint.MaxValue - currentRid)
                            throw new BadImageFormatException("CrossModuleInlineInfo inliner RID overflows UInt32.");

                        currentRid += inlinerDeltaAndFlag;
                    }
                    inliners.Add(new CrossModuleInlinerRef(isCrossModule: false, index: currentRid, moduleIndex: moduleIndex));
                }

                entries.Add(new CrossModuleInlineEntry(
                    crossModuleInlinee, inlineeIndex, inlineeModuleIndex, inliners));
                curParser = enumerator.GetNext();
            }

            return new CrossModuleInlineInfoTable(entries);
        }
    }

    /// <summary>
    /// A single entry in the CrossModuleInlineInfo table.
    /// </summary>
    public sealed class CrossModuleInlineEntry
    {
        /// <summary>Whether the inlinee is a cross-module reference (ILBody import index).</summary>
        public bool IsCrossModuleInlinee { get; }

        /// <summary>
        /// The inlinee index. If <see cref="IsCrossModuleInlinee"/> is true, this is an
        /// ILBody import section index. Otherwise, it's a MethodDef RID.
        /// </summary>
        public uint InlineeIndex { get; }

        /// <summary>Module index for local inlinees (0 = owner module).</summary>
        public uint InlineeModuleIndex { get; }

        /// <summary>List of inliner references.</summary>
        public IReadOnlyList<CrossModuleInlinerRef> Inliners { get; }

        internal CrossModuleInlineEntry(bool isCrossModuleInlinee, uint inlineeIndex, uint inlineeModuleIndex, List<CrossModuleInlinerRef> inliners)
        {
            IsCrossModuleInlinee = isCrossModuleInlinee;
            InlineeIndex = inlineeIndex;
            InlineeModuleIndex = inlineeModuleIndex;
            Inliners = inliners;
        }
    }

    /// <summary>
    /// A reference to an inliner method in the CrossModuleInlineInfo format.
    /// </summary>
    public sealed class CrossModuleInlinerRef
    {
        /// <summary>Whether this is a cross-module reference (ILBody import index).</summary>
        public bool IsCrossModule { get; }

        /// <summary>
        /// The method index. If <see cref="IsCrossModule"/> is true, this is an ILBody
        /// import section index. Otherwise, it's a MethodDef RID.
        /// </summary>
        public uint Index { get; }

        /// <summary>Module index for local inliners.</summary>
        public uint ModuleIndex { get; }

        internal CrossModuleInlinerRef(bool isCrossModule, uint index, uint moduleIndex)
        {
            IsCrossModule = isCrossModule;
            Index = index;
            ModuleIndex = moduleIndex;
        }
    }
}
