// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;


namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Structural projection of the InliningInfo section (v1, section 110, deprecated in 4.1).
    /// Contains an index of inlinee RIDs to nibble-encoded lists of inliner RIDs.
    /// No method name resolution is performed.
    /// </summary>
    /// <remarks>
    /// Crossgen2 emitter: <c>(legacy R2R V1 inlining info; not emitted by current Crossgen2 — produced by older R2R compilers)</c>.
    /// </remarks>
    public sealed class InliningInfoTable
    {
        public IReadOnlyList<InliningInfoEntry> Entries { get; }

        internal InliningInfoTable(List<InliningInfoEntry> entries)
        {
            Entries = entries;
        }
    }

    public partial class ReadyToRunReader
    {
        public InliningInfoTable GetInliningInfoTable(ReadyToRunSection section)
        {
            int startOffset = ValidateAndGetSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.InliningInfo,
                nameof(GetInliningInfoTable));
            if (section.Size < sizeof(int))
                throw new BadImageFormatException("InliningInfo section is missing its inlinee index size.");

            int offset = startOffset;
            int sizeOfInlineIndex = _nativeReader.ReadInt32(ref offset);
            if (sizeOfInlineIndex < 0 || sizeOfInlineIndex % (2 * sizeof(int)) != 0)
                throw new BadImageFormatException("InliningInfo inlinee index has an invalid size.");

            int sectionEndOffset = startOffset + section.Size;
            if (sizeOfInlineIndex > sectionEndOffset - offset)
                throw new BadImageFormatException("InliningInfo inlinee index extends beyond its section.");
            int inlineIndexEndOffset = offset + sizeOfInlineIndex;

            var entries = new List<InliningInfoEntry>();

            while (offset < inlineIndexEndOffset)
            {
                int inlineeRid = _nativeReader.ReadInt32(ref offset);
                int inlinersRelativeOffset = _nativeReader.ReadInt32(ref offset);
                long inlinersOffset = (long)inlineIndexEndOffset + inlinersRelativeOffset;
                if (inlinersOffset < inlineIndexEndOffset || inlinersOffset >= sectionEndOffset)
                    throw new BadImageFormatException("InliningInfo inliner-list offset is outside its section.");

                var handle = (InlinerListOffset)(uint)inlinersRelativeOffset;
                entries.Add(new InliningInfoEntry((MethodRid)inlineeRid, handle));
            }

            return new InliningInfoTable(entries);
        }

        /// <summary>
        /// Decode the nibble-encoded inliner RID list referenced by <paramref name="handle"/>.
        /// </summary>
        /// <param name="section">The InliningInfo section that <paramref name="handle"/> was read from.</param>
        /// <param name="handle">The encoded offset of the list, relative to the end of the inlinee index.</param>
        public IReadOnlyList<MethodRid> GetInliners(ReadyToRunSection section, InlinerListOffset handle)
        {
            EnsureSemanticDecodingSupported(nameof(GetInliners));
            int sectionOffset = ValidateAndGetSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.InliningInfo,
                nameof(GetInliners));
            if (section.Size < sizeof(int))
                throw new BadImageFormatException("InliningInfo section is missing its inlinee index size.");

            int offset = sectionOffset;
            int sizeOfInlineIndex = _nativeReader.ReadInt32(ref offset);
            if (sizeOfInlineIndex < 0 || sizeOfInlineIndex % (2 * sizeof(int)) != 0)
                throw new BadImageFormatException("InliningInfo inlinee index has an invalid size.");

            int sectionEndOffset = sectionOffset + section.Size;
            if (sizeOfInlineIndex > sectionEndOffset - offset)
                throw new BadImageFormatException("InliningInfo inlinee index extends beyond its section.");

            int inlineIndexEndOffset = offset + sizeOfInlineIndex;
            long listOffset = (long)inlineIndexEndOffset + (uint)handle;
            if (listOffset < inlineIndexEndOffset || listOffset >= sectionEndOffset)
                throw new BadImageFormatException("InliningInfo inliner-list offset is outside its section.");

            var nibbleReader = new NibbleReader(_nativeReader, (int)listOffset, sectionEndOffset);
            uint sameModuleCount = nibbleReader.ReadUInt();
            long availableNibbleCount = ((long)sectionEndOffset - listOffset) * 2;
            if (sameModuleCount > availableNibbleCount)
                throw new BadImageFormatException("InliningInfo inliner count exceeds the containing nibble stream.");

            var inlinerRids = new List<MethodRid>((int)System.Math.Min(sameModuleCount, 1024));
            int baseRid = 0;
            for (uint i = 0; i < sameModuleCount; i++)
            {
                uint delta = nibbleReader.ReadUInt();
                if (delta > int.MaxValue - baseRid)
                    throw new BadImageFormatException("InliningInfo inliner RID overflows Int32.");

                int currentRid = baseRid + (int)delta;
                inlinerRids.Add((MethodRid)currentRid);
                baseRid = currentRid;
            }

            return inlinerRids;
        }
    }

    /// <summary>
    /// A single entry in the v1 InliningInfo index.
    /// </summary>
    public sealed class InliningInfoEntry
    {
        /// <summary>MethodDef RID of the inlinee.</summary>
        public MethodRid InlineeRid { get; }

        /// <summary>
        /// Encoded offset of the nibble-encoded inliner RID list, relative to the end of the inlinee index.
        /// Resolve with <see cref="ReadyToRunReader.GetInliners"/> and the same InliningInfo section.
        /// </summary>
        public InlinerListOffset InlinersOffset { get; }

        internal InliningInfoEntry(MethodRid inlineeRid, InlinerListOffset inlinersOffset)
        {
            InlineeRid = inlineeRid;
            InlinersOffset = inlinersOffset;
        }
    }

    /// <summary>
    /// Opaque handle to a nibble-encoded inliner RID list in the v1 InliningInfo section.
    /// The underlying value is the encoded offset from the end of the inlinee index.
    /// </summary>
}
