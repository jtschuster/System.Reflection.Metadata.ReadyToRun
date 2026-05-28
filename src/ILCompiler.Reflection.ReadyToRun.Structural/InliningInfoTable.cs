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
            int startOffset = GetOffsetForRVA(section.RelativeVirtualAddress);
            int offset = startOffset;
            int sizeOfInlineIndex = _nativeReader.ReadInt32(ref offset);
            int inlineIndexEndOffset = offset + sizeOfInlineIndex;
            var entries = new List<InliningInfoEntry>();

            while (offset < inlineIndexEndOffset)
            {
                int inlineeRid = _nativeReader.ReadInt32(ref offset);
                int inlinersRelativeOffset = _nativeReader.ReadInt32(ref offset);
                var handle = (InlinerListOffset)(uint)(inlineIndexEndOffset + inlinersRelativeOffset);
                entries.Add(new InliningInfoEntry((MethodRid)inlineeRid, handle));
            }

            return new InliningInfoTable(entries);
        }

        /// <summary>
        /// Decode the nibble-encoded inliner RID list referenced by <paramref name="handle"/>.
        /// </summary>
        public IReadOnlyList<MethodRid> GetInliners(InlinerListOffset handle)
        {
            var nibbleReader = new NibbleReader(_nativeReader, (int)(uint)handle);
            uint sameModuleCount = nibbleReader.ReadUInt();

            var inlinerRids = new List<MethodRid>((int)sameModuleCount);
            int baseRid = 0;
            for (uint i = 0; i < sameModuleCount; i++)
            {
                int currentRid = baseRid + (int)nibbleReader.ReadUInt();
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

        /// <summary>Handle to the nibble-encoded inliner RID list. Resolve with <see cref="ReadyToRunReader.GetInliners"/>.</summary>
        public InlinerListOffset InlinersOffset { get; }

        internal InliningInfoEntry(MethodRid inlineeRid, InlinerListOffset inlinersOffset)
        {
            InlineeRid = inlineeRid;
            InlinersOffset = inlinersOffset;
        }
    }

    /// <summary>
    /// Opaque handle to a nibble-encoded inliner RID list in the v1 InliningInfo section.
    /// The underlying value is the absolute file offset of the nibble stream.
    /// </summary>
    public enum InlinerListOffset : uint { }
}
