// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;


namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Handle to the NativeHashtable stored in an InstanceMethodEntryPoints section, keyed by
    /// version-resilient method hashcode. Obtain one from
    /// <see cref="ReadyToRunReader.GetInstanceMethodEntryPointsTable(ReadyToRunSection)"/>.
    /// </summary>
    /// <remarks>
    /// <code>
    /// NativeHashtable bucket -> { low hash byte, entry }
    /// entry: method signature | DecodeUnsigned(id) | [DecodeUnsigned(fixup back-reference delta)]
    ///                           id bit 0 = has fixups, bit 1 = back-referenced fixup list,
    ///                           remaining bits = RuntimeFunctions index
    /// </code>
    /// Addressing composes by addition: <c>table start + <see cref="InstanceMethodEntryHandle"/> = entry image offset</c>.
    /// Crossgen2 emitter: <c>InstanceEntryPointTableNode</c>.
    /// </remarks>
    public readonly struct InstanceMethodHashTableHandle
    {
        internal int Offset { get; }

        internal int Size { get; }

        internal InstanceMethodHashTableHandle(int offset, int size)
        {
            Offset = offset;
            Size = size;
        }
    }

    /// <summary>
    /// One raw slot from an InstanceMethodEntryPoints hashtable bucket: the entry handle and the
    /// low byte of the version-resilient hashcode stored alongside it in the bucket.
    /// </summary>
    public readonly struct InstanceMethodHashTableEntry
    {
        /// <summary>Entry handle, relative to the start of the hashtable.</summary>
        public InstanceMethodEntryHandle Handle { get; }

        /// <summary>Low byte of the version-resilient hashcode, as stored in the bucket.</summary>
        public byte LowHashcode { get; }

        internal InstanceMethodHashTableEntry(InstanceMethodEntryHandle handle, byte lowHashcode)
        {
            Handle = handle;
            LowHashcode = lowHashcode;
        }
    }

    public partial class ReadyToRunReader
    {
        /// <summary>
        /// Open the InstanceMethodEntryPoints section as a NativeHashtable handle without decoding any entries.
        /// </summary>
        public InstanceMethodHashTableHandle GetInstanceMethodEntryPointsTable(ReadyToRunSection section)
        {
            int sectionOffset = ValidateAndGetNativeHashtableSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.InstanceMethodEntryPoints,
                nameof(GetInstanceMethodEntryPointsTable));
            return new InstanceMethodHashTableHandle(sectionOffset, section.Size);
        }

        /// <summary>
        /// Lazily enumerate every slot in the InstanceMethodEntryPoints hashtable.
        /// </summary>
        public IEnumerable<InstanceMethodHashTableEntry> EnumerateInstanceMethodEntries(InstanceMethodHashTableHandle table)
        {
            EnsureSemanticDecodingSupported(nameof(EnumerateInstanceMethodEntries));
            NativeHashtable hashtable = OpenNativeHashtable(table.Offset, table.Size, nameof(table), out int tableEndOffset);
            return Enumerate(hashtable, table.Offset, tableEndOffset);

            static IEnumerable<InstanceMethodHashTableEntry> Enumerate(NativeHashtable hashtable, int tableOffset, int tableEndOffset)
            {
                NativeHashtable.AllEntriesEnumerator enumerator = hashtable.EnumerateAllEntries();
                for (NativeParser curParser = enumerator.GetNext(); !curParser.IsNull(); curParser = enumerator.GetNext())
                {
                    var handle = (InstanceMethodEntryHandle)ToTableRelativeOffset(curParser.Offset, tableOffset, tableEndOffset);
                    yield return new InstanceMethodHashTableEntry(handle, curParser.LowHashcode);
                }
            }
        }

        /// <summary>
        /// Probe the bucket for <paramref name="versionResilientHash"/> and return the first entry whose
        /// method signature satisfies <paramref name="predicate"/>. Mirrors the runtime: entries are filtered
        /// by low hash byte, then the caller confirms the full signature match.
        /// </summary>
        /// <returns>The matching entry, or <see langword="null"/> if no entry matches.</returns>
        public InstanceMethodEntryHandle? LookupInstanceMethodEntry(
            InstanceMethodHashTableHandle table,
            int versionResilientHash,
            Func<MethodSignature, bool> predicate)
            => LookupInstanceMethodEntry(table, versionResilientHash, predicate, SignatureDecodingOptions);

        /// <summary>
        /// Look up an instance-method entry using explicit ReadyToRun version/policy settings.
        /// </summary>
        public InstanceMethodEntryHandle? LookupInstanceMethodEntry(
            InstanceMethodHashTableHandle table,
            int versionResilientHash,
            Func<MethodSignature, bool> predicate,
            IReadyToRunSignatureDecodingOptions options)
        {
            EnsureSemanticDecodingSupported(nameof(LookupInstanceMethodEntry));
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(options);

            NativeHashtable hashtable = OpenNativeHashtable(table.Offset, table.Size, nameof(table), out int tableEndOffset);
            NativeHashtable.Enumerator enumerator = hashtable.Lookup(versionResilientHash);

            for (NativeParser entryParser = enumerator.GetNext(); !entryParser.IsNull(); entryParser = enumerator.GetNext())
            {
                uint relativeOffset = ToTableRelativeOffset(entryParser.Offset, table.Offset, tableEndOffset);
                R2RSignatureDecodeResult signature = RawSignatureDecoder.DecodeMethodSignatureWithEndOffset(
                    _nativeReader, (int)entryParser.Offset, TargetPointerSize, options);
                EnsureOffsetWithinRange(signature.EndOffset, table.Offset, tableEndOffset);
                if (predicate(MethodSignature.FromSignature(signature.Signature)))
                    return (InstanceMethodEntryHandle)relativeOffset;
            }

            return null;
        }

        /// <summary>
        /// Decode an instance-method entry: the method signature, followed by the runtime-function index
        /// and optional fixup-list handle.
        /// </summary>
        public InstanceMethodEntry GetInstanceMethodEntry(InstanceMethodHashTableHandle table, InstanceMethodEntryHandle handle)
            => GetInstanceMethodEntry(table, handle, SignatureDecodingOptions);

        /// <summary>
        /// Decode an instance-method entry using explicit ReadyToRun version/policy settings.
        /// </summary>
        public InstanceMethodEntry GetInstanceMethodEntry(
            InstanceMethodHashTableHandle table,
            InstanceMethodEntryHandle handle,
            IReadyToRunSignatureDecodingOptions options)
        {
            EnsureSemanticDecodingSupported(nameof(GetInstanceMethodEntry));
            ArgumentNullException.ThrowIfNull(options);

            _ = OpenNativeHashtable(table.Offset, table.Size, nameof(table), out int tableEndOffset);
            int entryOffset = ToTableEntryImageOffset((uint)handle, table.Offset, tableEndOffset);
            R2RSignatureDecodeResult signature = RawSignatureDecoder.DecodeMethodSignatureWithEndOffset(_nativeReader, entryOffset, TargetPointerSize, options);
            EnsureOffsetWithinRange(signature.EndOffset, table.Offset, tableEndOffset);

            (RuntimeFunctionIndex runtimeFunctionIndex, FixupCellListHandle? fixupCellList) =
                DecodeRuntimeFunctionIdAndFixupCellList(signature.EndOffset, table.Offset, tableEndOffset);
            return new InstanceMethodEntry(signature.Signature, runtimeFunctionIndex, fixupCellList);
        }

        /// <summary>
        /// Shared decode for MethodDefEntry/InstanceMethodEntry payload tail:
        /// compressed "id" (bit 0 = has-fixups, bit 1 = back-reference), followed by optional fixup list data.
        /// </summary>
        internal (RuntimeFunctionIndex, FixupCellListHandle?) DecodeRuntimeFunctionIdAndFixupCellList(
            int offset,
            int containingStartOffset,
            int containingEndOffset)
        {
            var parser = new NativeParser(
                _nativeReader,
                (uint)offset,
                (uint)containingEndOffset);
            int encodedIdOffset = offset;
            uint id = parser.GetUnsigned();
            offset = (int)parser.Offset;

            FixupCellListHandle? fixupCells = null;
            RuntimeFunctionIndex runtimeFunctionIndex;

            if ((id & 1) != 0)
            {
                if ((id & 2) != 0)
                {
                    int backReferenceOffset = (int)parser.Offset;
                    uint delta = parser.GetUnsigned();
                    if (delta > (uint)(backReferenceOffset - containingStartOffset))
                        throw new BadImageFormatException("Fixup-cell list back-reference points outside its containing section.");
                }

                fixupCells = (FixupCellListHandle)(uint)(encodedIdOffset - containingStartOffset);
                runtimeFunctionIndex = (RuntimeFunctionIndex)(id >> 2);
            }
            else
            {
                runtimeFunctionIndex = (RuntimeFunctionIndex)(id >> 1);
            }

            return (runtimeFunctionIndex, fixupCells);
        }

        /// <summary>
        /// Decodes the nibble-encoded fixup cell list referenced by a method entry payload.
        /// </summary>
        public IReadOnlyList<FixupCellRef> GetFixupCells(
            ReadyToRunSection section,
            FixupCellListHandle fixupCellList)
        {
            EnsureSemanticDecodingSupported(nameof(GetFixupCells));
            int sectionOffset = ValidateFixupCellListSection(section, nameof(GetFixupCells));
            int sectionEndOffset = checked(sectionOffset + section.Size);

            int fixupOffset = (int)GetFixupCellListImageOffset(section, fixupCellList);

            var fixupCells = new List<FixupCellRef>();

            NibbleReader nibbleReader = new NibbleReader(
                _nativeReader,
                fixupOffset,
                sectionEndOffset);
            uint curTableIndex = nibbleReader.ReadUInt();

            while (true)
            {
                uint cellIndex = nibbleReader.ReadUInt();

                while (true)
                {
                    fixupCells.Add(new FixupCellRef((ImportSectionIndex)curTableIndex, (ImportCellIndex)cellIndex));

                    uint delta = nibbleReader.ReadUInt();
                    if (delta == 0)
                        break;

                    if (delta > uint.MaxValue - cellIndex)
                        throw new BadImageFormatException("Fixup-cell index overflows UInt32.");
                    cellIndex += delta;
                }

                uint tableDelta = nibbleReader.ReadUInt();
                if (tableDelta == 0)
                    break;

                if (tableDelta > uint.MaxValue - curTableIndex)
                    throw new BadImageFormatException("Fixup-cell table index overflows UInt32.");
                curTableIndex += tableDelta;
            }

            return fixupCells;
        }

        /// <summary>
        /// Resolve an encoded fixup-list reference to the image offset of its nibble-encoded data.
        /// </summary>
        /// <param name="section">The method entry-point section that contains the handle.</param>
        /// <param name="fixupCellList">The encoded reference handle from a method entry.</param>
        public FixupCellListImageOffset GetFixupCellListImageOffset(
            ReadyToRunSection section,
            FixupCellListHandle fixupCellList)
        {
            EnsureSemanticDecodingSupported(nameof(GetFixupCellListImageOffset));
            int sectionOffset = ValidateFixupCellListSection(section, nameof(GetFixupCellListImageOffset));
            int sectionEndOffset = checked(sectionOffset + section.Size);
            if ((uint)fixupCellList >= (uint)section.Size)
                throw new BadImageFormatException("Fixup-list reference offset is outside its section.");

            var parser = new NativeParser(
                _nativeReader,
                checked((uint)(sectionOffset + (int)(uint)fixupCellList)),
                (uint)sectionEndOffset);
            uint id = parser.GetUnsigned();
            if ((id & 1) == 0)
                throw new BadImageFormatException("Entry-point payload does not contain a fixup list.");

            int fixupOffset = (int)parser.Offset;
            if ((id & 2) != 0)
            {
                int backReferenceOffset = (int)parser.Offset;
                uint delta = parser.GetUnsigned();
                if (delta > (uint)(backReferenceOffset - sectionOffset))
                    throw new BadImageFormatException("Fixup-cell list back-reference points outside its containing section.");
                fixupOffset = checked(backReferenceOffset - (int)delta);
            }

            return (FixupCellListImageOffset)fixupOffset;
        }

        private int ValidateFixupCellListSection(ReadyToRunSection section, string parameterName)
            => section switch
            {
                KnownReadyToRunSection { Type: Internal.Runtime.ReadyToRunSectionType.MethodDefEntryPoints } => ValidateAndGetSectionOffset(
                    section,
                    Internal.Runtime.ReadyToRunSectionType.MethodDefEntryPoints,
                    parameterName),
                KnownReadyToRunSection { Type: Internal.Runtime.ReadyToRunSectionType.InstanceMethodEntryPoints } => ValidateAndGetSectionOffset(
                    section,
                    Internal.Runtime.ReadyToRunSectionType.InstanceMethodEntryPoints,
                    parameterName),
                _ => throw new ArgumentException("Section must contain method entry points.", parameterName),
            };

        private static void EnsureOffsetWithinRange(int offset, int startOffset, int endOffset)
        {
            if (offset < startOffset || offset >= endOffset)
                throw new BadImageFormatException("Payload extends beyond its containing range.");
        }
    }

    /// <summary>
    /// Decoded InstanceMethodEntryPoints hashtable entry: the method reference, the runtime function
    /// index of its entry point, and a handle to any fixup cell references.
    /// </summary>
    /// <remarks>
    /// <code>
    /// method signature | DecodeUnsigned(id) | [DecodeUnsigned(back-reference delta)]
    /// </code>
    /// </remarks>
    public sealed class InstanceMethodEntry
    {
        /// <summary>Raw method signature parts decoded from the start of the entry.</summary>
        public R2RSignature MethodSignature { get; }

        /// <summary>Index into the RuntimeFunctions table.</summary>
        public RuntimeFunctionIndex EntryPointIndex { get; }

        /// <summary>
        /// Handle to the encoded fixup-list reference, relative to the InstanceMethodEntryPoints section;
        /// resolve with <see cref="ReadyToRunReader.GetFixupCells"/>.
        /// </summary>
        public FixupCellListHandle? FixupCellList { get; }

        internal InstanceMethodEntry(
            R2RSignature methodSignature,
            RuntimeFunctionIndex entryPointIndex,
            FixupCellListHandle? fixupCellList)
        {
            MethodSignature = methodSignature;
            EntryPointIndex = entryPointIndex;
            FixupCellList = fixupCellList;
        }
    }
}
