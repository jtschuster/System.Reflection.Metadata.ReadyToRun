// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;


namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Structural projection of the InstanceMethodEntryPoints section: a NativeHashtable keyed by
    /// version-resilient method hashcode. Entries are not eagerly decoded; resolve through the
    /// reader with <see cref="ReadyToRunReader.LookupInstanceMethodEntryPoint"/> for a keyed probe
    /// or <see cref="ReadyToRunReader.EnumerateInstanceMethodEntries"/> for a full scan.
    /// </summary>
    /// <remarks>
    /// Crossgen2 emitter: <c>InstanceEntryPointTableNode</c>.
    /// </remarks>
    public sealed class InstanceMethodEntryPointsTable
    {
        internal ImageRVA SectionRva { get; }
        internal int SectionSize { get; }

        internal InstanceMethodEntryPointsTable(ImageRVA sectionRva, int sectionSize)
        {
            SectionRva = sectionRva;
            SectionSize = sectionSize;
        }
    }

    public partial class ReadyToRunReader
    {
        /// <summary>
        /// Open the InstanceMethodEntryPoints section as an inert table that can be used for
        /// keyed lookups or full enumeration without eagerly decoding every entry.
        /// </summary>
        public InstanceMethodEntryPointsTable GetInstanceMethodEntryPointsTable(ReadyToRunSection section)
        {
            ValidateAndGetSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.InstanceMethodEntryPoints,
                nameof(GetInstanceMethodEntryPointsTable));

            return new InstanceMethodEntryPointsTable(section.RelativeVirtualAddress, section.Size);
        }

        private NativeHashtable OpenInstanceMethodEntryPointsHashtable(
            InstanceMethodEntryPointsTable table,
            out int sectionOffset,
            out int sectionEndOffset)
        {
            var section = new ReadyToRunSection(
                Internal.Runtime.ReadyToRunSectionType.InstanceMethodEntryPoints,
                table.SectionRva,
                table.SectionSize);
            sectionOffset = ValidateAndGetSectionOffset(section);
            sectionEndOffset = checked(sectionOffset + table.SectionSize);
            NativeParser parser = new NativeParser(_nativeReader, (uint)sectionOffset);
            return new NativeHashtable(_nativeReader, parser, (uint)sectionEndOffset);
        }

        /// <summary>
        /// Look up an instance-method entry by version-resilient hashcode and signature predicate.
        /// Mirrors the runtime VM's pattern of probing the NativeHashtable bucket for entries with
        /// a matching low-byte hash, decoding each candidate's signature, and asking the caller's
        /// predicate to confirm the full match (signature equality is decided by the caller because
        /// only it knows the original key being resolved).
        /// </summary>
        /// <returns>The decoded payload for the first matching entry, or <c>null</c> if no entry matches.</returns>
        public InstanceMethodPayload LookupInstanceMethodEntryPoint(InstanceMethodEntryPointsTable table, int versionResilientHash, Func<MethodSignature, bool> predicate)
            => LookupInstanceMethodEntryPoint(table, versionResilientHash, predicate, SignatureDecodingOptions);

        /// <summary>
        /// Look up an instance-method entry using explicit ReadyToRun version/policy settings.
        /// </summary>
        public InstanceMethodPayload LookupInstanceMethodEntryPoint(
            InstanceMethodEntryPointsTable table,
            int versionResilientHash,
            Func<MethodSignature, bool> predicate,
            IReadyToRunSignatureDecodingOptions options)
        {
            EnsureSemanticDecodingSupported(nameof(LookupInstanceMethodEntryPoint));
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(options);

            NativeHashtable hashtable = OpenInstanceMethodEntryPointsHashtable(
                table,
                out int sectionOffset,
                out int sectionEndOffset);
            NativeHashtable.Enumerator enumerator = hashtable.Lookup(versionResilientHash);

            NativeParser entryParser = enumerator.GetNext();
            while (!entryParser.IsNull())
            {
                int payloadOffset = (int)entryParser.Offset;
                EnsureOffsetWithinRange(payloadOffset, sectionOffset, sectionEndOffset);
                R2RSignatureDecodeResult signature = RawSignatureDecoder.DecodeMethodSignatureWithEndOffset(_nativeReader, payloadOffset, TargetPointerSize, options);
                EnsureOffsetWithinRange(signature.EndOffset, sectionOffset, sectionEndOffset);
                MethodSignature methodSig = MethodSignature.FromSignature(signature.Signature);
                if (predicate(methodSig))
                {
                    (RuntimeFunctionIndex runtimeFunctionIndex, FixupCellListHandle? fixupCellListHandle) =
                        DecodeRuntimeFunctionIdAndFixupCellList(signature.EndOffset, sectionOffset, sectionEndOffset);
                    return new InstanceMethodPayload(signature.Signature, runtimeFunctionIndex, fixupCellListHandle);
                }
                entryParser = enumerator.GetNext();
            }

            return null;
        }

        /// <summary>
        /// Lazily enumerate every entry in the InstanceMethodEntryPoints hashtable. Each yielded
        /// <see cref="InstanceMethodEntry"/> carries only a payload offset and a low hashcode byte;
        /// call <see cref="GetInstanceMethodPayload(InstanceMethodEntryPointsTable, InstanceMethodEntry)"/> to decode the signature
        /// and entry-point data on demand.
        /// </summary>
        public IEnumerable<InstanceMethodEntry> EnumerateInstanceMethodEntries(InstanceMethodEntryPointsTable table)
        {
            EnsureSemanticDecodingSupported(nameof(EnumerateInstanceMethodEntries));
            NativeHashtable hashtable = OpenInstanceMethodEntryPointsHashtable(
                table,
                out int sectionOffset,
                out int sectionEndOffset);
            NativeHashtable.AllEntriesEnumerator enumerator = hashtable.EnumerateAllEntries();

            for (NativeParser curParser = enumerator.GetNext(); !curParser.IsNull(); curParser = enumerator.GetNext())
            {
                EnsureOffsetWithinRange((int)curParser.Offset, sectionOffset, sectionEndOffset);
                yield return new InstanceMethodEntry((InstanceMethodPayloadOffset)curParser.Offset, curParser.LowHashcode);
            }
        }

        /// <summary>
        /// Fully parse an <see cref="InstanceMethodEntry"/>: decode the method signature,
        /// followed by the inline runtime-function-index and optional fixup-list handle. The payload
        /// layout is method-signature || DecodeUnsigned(id) || optional back-reference.
        /// </summary>
        public InstanceMethodPayload GetInstanceMethodPayload(
            InstanceMethodEntryPointsTable table,
            InstanceMethodEntry entry)
            => GetInstanceMethodPayload(table, entry, SignatureDecodingOptions);

        /// <summary>
        /// Fully parse an <see cref="InstanceMethodEntry"/> using explicit ReadyToRun version/policy settings.
        /// </summary>
        public InstanceMethodPayload GetInstanceMethodPayload(
            InstanceMethodEntryPointsTable table,
            InstanceMethodEntry entry,
            IReadyToRunSignatureDecodingOptions options)
        {
            EnsureSemanticDecodingSupported(nameof(GetInstanceMethodPayload));
            ArgumentNullException.ThrowIfNull(table);
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(options);

            _ = OpenInstanceMethodEntryPointsHashtable(table, out int sectionOffset, out int sectionEndOffset);
            int payloadOffset = (int)entry.PayloadOffset;
            EnsureOffsetWithinRange(payloadOffset, sectionOffset, sectionEndOffset);
            R2RSignatureDecodeResult signature = RawSignatureDecoder.DecodeMethodSignatureWithEndOffset(_nativeReader, payloadOffset, TargetPointerSize, options);
            EnsureOffsetWithinRange(signature.EndOffset, sectionOffset, sectionEndOffset);

            (RuntimeFunctionIndex runtimeFunctionIndex, FixupCellListHandle? fixupCellListHandle) =
                DecodeRuntimeFunctionIdAndFixupCellList(signature.EndOffset, sectionOffset, sectionEndOffset);
            return new InstanceMethodPayload(signature.Signature, runtimeFunctionIndex, fixupCellListHandle);
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
                    uint delta = parser.GetUnsigned();
                    if (delta > parser.Offset - (uint)containingStartOffset)
                        throw new BadImageFormatException("Fixup-cell list back-reference points outside its containing section.");
                }

                fixupCells = new FixupCellListHandle(
                    (FixupCellListOffset)(uint)(encodedIdOffset - containingStartOffset));
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
            int sectionOffset = section.Type switch
            {
                Internal.Runtime.ReadyToRunSectionType.MethodDefEntryPoints => ValidateAndGetSectionOffset(
                    section,
                    Internal.Runtime.ReadyToRunSectionType.MethodDefEntryPoints,
                    nameof(GetFixupCells)),
                Internal.Runtime.ReadyToRunSectionType.InstanceMethodEntryPoints => ValidateAndGetSectionOffset(
                    section,
                    Internal.Runtime.ReadyToRunSectionType.InstanceMethodEntryPoints,
                    nameof(GetFixupCells)),
                _ => throw new ArgumentException("Section must contain method entry points.", nameof(section)),
            };
            int sectionEndOffset = checked(sectionOffset + section.Size);
            if ((uint)fixupCellList.Offset >= (uint)section.Size)
                throw new BadImageFormatException("Fixup-list reference offset is outside its section.");

            var parser = new NativeParser(
                _nativeReader,
                checked((uint)(sectionOffset + (int)(uint)fixupCellList.Offset)),
                (uint)sectionEndOffset);
            uint id = parser.GetUnsigned();
            if ((id & 1) == 0)
                throw new BadImageFormatException("Entry-point payload does not contain a fixup list.");

            int fixupOffset = (int)parser.Offset;
            if ((id & 2) != 0)
            {
                uint delta = parser.GetUnsigned();
                if (delta > parser.Offset - (uint)sectionOffset)
                    throw new BadImageFormatException("Fixup-cell list back-reference points outside its containing section.");
                fixupOffset = checked((int)(parser.Offset - delta));
            }

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

        private static void EnsureOffsetWithinRange(int offset, int startOffset, int endOffset)
        {
            if (offset < startOffset || offset >= endOffset)
                throw new BadImageFormatException("Payload extends beyond its containing range.");
        }
    }

    /// <summary>
    /// Fully decoded payload for an <see cref="InstanceMethodEntry"/>:
    /// the method reference, the runtime function index of its entry point,
    /// and a handle to any fixup cell references.
    /// </summary>
    public sealed class InstanceMethodPayload
    {
        /// <summary>Raw method signature parts decoded from the start of the entry payload.</summary>
        public R2RSignature MethodSignature { get; }
        public RuntimeFunctionIndex EntryPointIndex { get; }
        public FixupCellListHandle? FixupCellListHandle { get; }

        internal InstanceMethodPayload(R2RSignature methodSignature, RuntimeFunctionIndex entryPointIndex, FixupCellListHandle? fixupCellListHandle)
        {
            MethodSignature = methodSignature;
            EntryPointIndex = entryPointIndex;
            FixupCellListHandle = fixupCellListHandle;
        }
    }

    public readonly struct FixupCellListHandle
    {
        /// <summary>Offset of the encoded reference, relative to its entry-point section.</summary>
        public FixupCellListOffset Offset { get; }

        internal FixupCellListHandle(FixupCellListOffset offset) => Offset = offset;
    }

    /// <summary>
    /// Opaque handle to the start of an <see cref="InstanceMethodEntry"/> payload
    /// (a method signature blob immediately followed by entry-point and fixup data).
    /// Pass with its <see cref="InstanceMethodEntryPointsTable"/> to
    /// <see cref="ReadyToRunReader.GetInstanceMethodPayload(InstanceMethodEntryPointsTable, InstanceMethodEntry)"/> to decode.
    /// The underlying value is the file offset (not RVA) of the payload start.
    /// </summary>


    /// <summary>
    /// A single entry in the InstanceMethodEntryPoints hashtable.
    /// Contains a handle to the signature blob for this generic method instantiation.
    /// The signature must be decoded by a higher-level reader to extract the
    /// runtime function index and fixup cells.
    /// </summary>
    public sealed class InstanceMethodEntry
    {
        /// <summary>Handle to the entry's payload (method signature blob followed by entry-point data).</summary>
        public InstanceMethodPayloadOffset PayloadOffset { get; }

        /// <summary>Low byte of the hash code used for hashtable bucketing.</summary>
        public byte LowHashcode { get; }

        internal InstanceMethodEntry(InstanceMethodPayloadOffset payloadOffset, byte lowHashcode)
        {
            PayloadOffset = payloadOffset;
            LowHashcode = lowHashcode;
        }
    }
}
