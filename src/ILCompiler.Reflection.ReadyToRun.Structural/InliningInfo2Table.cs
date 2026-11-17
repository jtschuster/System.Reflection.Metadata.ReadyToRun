// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Structural projection of the InliningInfo2 section (section 114, v4.1+).
    /// A NativeHashtable of inlining entries. Each entry maps an inlinee
    /// (identified by RID and optional module index) to a list of inliners.
    /// No method name resolution is performed.
    ///
    /// Scope: this section lives in the per-component R2R header. In a
    /// non-composite image there is a single component header (the image header)
    /// so the table is image-wide. In a composite image each component assembly
    /// has its own InliningInfo2 table; module index 0 within an entry refers
    /// to that component assembly (the "owner"), not the image.
    /// </summary>
    /// <remarks>
    /// Crossgen2 emitter: <c>InliningInfoNode (InfoType.InliningInfo2)</c>.
    /// <para>All values are compressed unsigned integers (<c>uint*</c>).</para>
    /// <code>
    /// InliningInfo2 section
    /// +-----------------------------------------------------------+
    /// | NativeHashtable                                           |
    /// |   key: owner-module hash XOR inlinee MethodDef token      |
    /// |   each entry points at one inlinee record (below)         |
    /// +-----------------------------------------------------------+
    ///
    /// Inlinee record
    /// +-----------------------------------------------------------+
    /// | count     uint*  number of uint* values that follow       |
    /// +-----------------------------------------------------------+
    /// | inlinee   uint*  bits 1+ = MethodDef RID                  |
    /// |                  bit 0   = module index follows           |
    /// | [module]  uint*  only if bit 0 was set                    |
    /// +-----------------------------------------------------------+
    /// | inliner   uint*  bits 1+ = RID delta from previous one    |
    /// |                  bit 0   = module index follows           |
    /// | [module]  uint*  only if bit 0 was set                    |
    /// | ... repeated until count values have been read ...        |
    /// +-----------------------------------------------------------+
    /// </code>
    /// <para>
    /// Example: inlinee RID 5 inlined into RIDs 10 and 12, all in the owner module, is stored as
    /// <c>[3] [5&lt;&lt;1 = 10] [10&lt;&lt;1 = 20] [2&lt;&lt;1 = 4]</c>. A missing module index means the owner module
    /// (<see langword="null"/> <see cref="ModuleIndex"/>).
    /// </para>
    /// <c>count</c> is the number of unsigned values that follow, including the inlinee value and module indices.
    /// </remarks>
    public readonly struct InliningInfo2HashTableHandle
    {
        internal int Offset { get; }

        internal int Size { get; }

        internal InliningInfo2HashTableHandle(int offset, int size)
        {
            Offset = offset;
            Size = size;
        }
    }

    public partial class ReadyToRunReader
    {
        /// <summary>
        /// Open the InliningInfo2 section as a NativeHashtable handle without decoding any entries.
        /// </summary>
        public InliningInfo2HashTableHandle GetInliningInfo2Table(ReadyToRunSection section)
        {
            int sectionOffset = ValidateAndGetNativeHashtableSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.InliningInfo2,
                nameof(GetInliningInfo2Table));
            return new InliningInfo2HashTableHandle(sectionOffset, section.Size);
        }

        /// <summary>Lazily enumerate every entry in the InliningInfo2 hashtable.</summary>
        public IEnumerable<InliningInfo2EntryHandle> EnumerateInliningInfo2Entries(InliningInfo2HashTableHandle table)
        {
            foreach (uint entry in EnumerateNativeHashtableEntries(table.Offset, table.Size, nameof(table)))
                yield return (InliningInfo2EntryHandle)entry;
        }

        /// <summary>
        /// Probe the bucket for <paramref name="hashcode"/> and return the first entry that satisfies
        /// <paramref name="predicate"/>.
        /// </summary>
        public InliningInfo2EntryHandle? LookupInliningInfo2Entry(
            InliningInfo2HashTableHandle table,
            int hashcode,
            Func<InliningInfo2Entry, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            foreach (uint entry in LookupNativeHashtableEntries(table.Offset, table.Size, hashcode, nameof(table)))
            {
                InliningInfo2EntryHandle handle = (InliningInfo2EntryHandle)entry;
                if (predicate(GetInliningInfo2Entry(table, handle)))
                    return handle;
            }

            return null;
        }

        /// <summary>Decode one InliningInfo2 entry and its inliner list.</summary>
        public InliningInfo2Entry GetInliningInfo2Entry(InliningInfo2HashTableHandle table, InliningInfo2EntryHandle handle)
        {
            NativeParser curParser = OpenNativeHashtableEntry(table.Offset, table.Size, (uint)handle, nameof(table), out _);
            uint encodedItemCount = curParser.GetUnsigned();
            if (encodedItemCount == 0)
                throw new BadImageFormatException("InliningInfo2 entry has an empty payload.");

            uint remainingItemCount = encodedItemCount - 1;
            uint inlineeRidAndFlag = curParser.GetUnsigned();

            int inlineeRid = (int)(inlineeRidAndFlag >> 1);
            ModuleIndex? inlineeModuleIndex = null;

            if ((inlineeRidAndFlag & 1) != 0)
            {
                if (remainingItemCount == 0)
                    throw new BadImageFormatException("InliningInfo2 inlinee module index is missing.");

                inlineeModuleIndex = (ModuleIndex)curParser.GetUnsigned();
                remainingItemCount--;
            }

            var inliners = new List<InlinerRef>();
            int currentRid = 0;

            while (remainingItemCount > 0)
            {
                uint inlinerDeltaAndFlag = curParser.GetUnsigned();
                remainingItemCount--;
                uint inlinerDelta = inlinerDeltaAndFlag >> 1;
                if (inlinerDelta > int.MaxValue - currentRid)
                    throw new BadImageFormatException("InliningInfo2 inliner RID overflows Int32.");

                currentRid += (int)inlinerDelta;

                ModuleIndex? inlinerModuleIndex = null;
                if ((inlinerDeltaAndFlag & 1) != 0)
                {
                    if (remainingItemCount == 0)
                        throw new BadImageFormatException("InliningInfo2 inliner module index is missing.");

                    inlinerModuleIndex = (ModuleIndex)curParser.GetUnsigned();
                    remainingItemCount--;
                }

                inliners.Add(new InlinerRef((MethodRid)currentRid, inlinerModuleIndex));
            }

            return new InliningInfo2Entry((MethodRid)inlineeRid, inlineeModuleIndex, inliners);
        }
    }

    /// <summary>
    /// A single decoded entry in the InliningInfo2 table.
    /// </summary>
    public sealed class InliningInfo2Entry
    {

        /// <summary>MethodDef RID of the inlinee.</summary>
        public MethodRid InlineeRid { get; }

        /// <summary>Encoded module index of the inlinee, or <see langword="null"/> for the owner module.</summary>
        public ModuleIndex? InlineeModuleIndex { get; }

        /// <summary>List of inliner method references.</summary>
        public IReadOnlyList<InlinerRef> Inliners { get; }

        internal InliningInfo2Entry(MethodRid inlineeRid, ModuleIndex? inlineeModuleIndex, List<InlinerRef> inliners)
        {
            InlineeRid = inlineeRid;
            InlineeModuleIndex = inlineeModuleIndex;
            Inliners = inliners;
        }
    }

    /// <summary>
    /// A reference to an inliner method in the InliningInfo2 format.
    /// </summary>
    public sealed class InlinerRef
    {
        /// <summary>MethodDef RID of the inliner.</summary>
        public MethodRid Rid { get; }

        /// <summary>Encoded module index of the inliner, or <see langword="null"/> for the owner module.</summary>
        public ModuleIndex? ModuleIndex { get; }

        internal InlinerRef(MethodRid rid, ModuleIndex? moduleIndex)
        {
            Rid = rid;
            ModuleIndex = moduleIndex;
        }
    }
}
