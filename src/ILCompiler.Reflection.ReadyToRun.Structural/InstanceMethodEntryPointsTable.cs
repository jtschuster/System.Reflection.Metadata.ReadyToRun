// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;


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
        private Dictionary<int, (int StartOffset, int EndOffset)> _payloadRanges;
        private Dictionary<FixupCellListHandle, (int StartOffset, int EndOffset)> _fixupCellListRanges;

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
                RegisterPayloadRange(payloadOffset, sectionOffset, sectionEndOffset);
                R2RSignatureDecodeResult signature = RawSignatureDecoder.DecodeMethodSignatureWithEndOffset(_nativeReader, payloadOffset, TargetPointerSize, options);
                EnsurePayloadOffsetWithinRange(signature.EndOffset, payloadOffset, allowEndOffset: false);
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
        /// call <see cref="GetInstanceMethodPayload(InstanceMethodEntry)"/> to decode the signature
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
                RegisterPayloadRange((int)curParser.Offset, sectionOffset, sectionEndOffset);
                yield return new InstanceMethodEntry((InstanceMethodPayloadOffset)curParser.Offset, curParser.LowHashcode);
            }
        }

        /// <summary>
        /// Fully parse an <see cref="InstanceMethodEntry"/>: decode the method signature,
        /// followed by the inline runtime-function-index and optional fixup-list handle. The payload
        /// layout is method-signature || DecodeUnsigned(id) || optional back-reference.
        /// </summary>
        public InstanceMethodPayload GetInstanceMethodPayload(InstanceMethodEntry entry)
            => GetInstanceMethodPayload(entry, SignatureDecodingOptions);

        /// <summary>
        /// Fully parse an <see cref="InstanceMethodEntry"/> using explicit ReadyToRun version/policy settings.
        /// </summary>
        public InstanceMethodPayload GetInstanceMethodPayload(InstanceMethodEntry entry, IReadyToRunSignatureDecodingOptions options)
        {
            EnsureSemanticDecodingSupported(nameof(GetInstanceMethodPayload));
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(options);

            int payloadOffset = (int)entry.PayloadOffset;
            (int sectionOffset, int sectionEndOffset) = GetPayloadRange(payloadOffset, nameof(entry));
            R2RSignatureDecodeResult signature = RawSignatureDecoder.DecodeMethodSignatureWithEndOffset(_nativeReader, payloadOffset, TargetPointerSize, options);
            EnsurePayloadOffsetWithinRange(signature.EndOffset, payloadOffset, allowEndOffset: false);

            int offset = signature.EndOffset;
            (RuntimeFunctionIndex runtimeFunctionIndex, FixupCellListHandle? fixupCellListHandle) =
                DecodeRuntimeFunctionIdAndFixupCellList(offset, sectionOffset, sectionEndOffset);
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
            uint id = parser.GetUnsigned();
            offset = (int)parser.Offset;

            FixupCellListHandle? fixupCells = null;
            RuntimeFunctionIndex runtimeFunctionIndex;

            if ((id & 1) != 0)
            {
                uint? backReferenceDelta = null;
                int fixupOffset = offset;

                if ((id & 2) != 0)
                {
                    uint delta = parser.GetUnsigned();
                    backReferenceDelta = delta;
                    if (delta > (uint)(offset - containingStartOffset))
                        throw new BadImageFormatException("Fixup-cell list back-reference points outside its containing section.");
                    fixupOffset = offset - (int)delta;
                }

                fixupCells = new FixupCellListHandle(fixupOffset, backReferenceDelta);
                _fixupCellListRanges ??= new Dictionary<FixupCellListHandle, (int, int)>();
                _fixupCellListRanges[fixupCells.Value] = (containingStartOffset, containingEndOffset);
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
        public IReadOnlyList<FixupCellRef> GetFixupCells(FixupCellListHandle fixupCellList)
        {
            EnsureSemanticDecodingSupported(nameof(GetFixupCells));
            if (_fixupCellListRanges is null
                || !_fixupCellListRanges.TryGetValue(fixupCellList, out var range))
            {
                throw new ArgumentException(
                    "The fixup-cell list handle was not created by this ReadyToRunReader.",
                    nameof(fixupCellList));
            }

            var fixupCells = new List<FixupCellRef>();

            NibbleReader nibbleReader = new NibbleReader(
                _nativeReader,
                fixupCellList.Offset,
                range.EndOffset);
            uint curTableIndex = nibbleReader.ReadUInt();

            while (true)
            {
                uint cellIndex = nibbleReader.ReadUInt();

                while (true)
                {
                    fixupCells.Add(new FixupCellRef(curTableIndex, cellIndex));

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

        private void RegisterPayloadRange(int payloadOffset, int startOffset, int endOffset)
        {
            if (payloadOffset < startOffset || payloadOffset >= endOffset)
                throw new BadImageFormatException("Payload offset is outside its containing section.");

            _payloadRanges ??= new Dictionary<int, (int, int)>();
            _payloadRanges[payloadOffset] = (startOffset, endOffset);
        }

        private (int StartOffset, int EndOffset) GetPayloadRange(int payloadOffset, string parameterName)
        {
            if (_payloadRanges is null || !_payloadRanges.TryGetValue(payloadOffset, out var range))
            {
                throw new ArgumentException(
                    "The payload handle was not created by this ReadyToRunReader.",
                    parameterName);
            }

            return range;
        }

        private void EnsurePayloadOffsetWithinRange(int offset, int payloadOffset, bool allowEndOffset)
        {
            (int startOffset, int endOffset) = GetPayloadRange(payloadOffset, nameof(payloadOffset));
            bool valid = offset >= startOffset
                && (allowEndOffset ? offset <= endOffset : offset < endOffset);
            if (!valid)
                throw new BadImageFormatException("Payload extends beyond its containing section.");
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
        internal int Offset { get; }

        /// <summary>Encoded back-reference delta when <see cref="IsBackReference"/> is true; otherwise null.</summary>
        internal uint? BackReferenceDelta { get; }

        /// <summary>True when the method payload stores a delta to a previous fixup list instead of an inline list.</summary>
        [MemberNotNullWhen(true, nameof(BackReferenceDelta))]
        internal bool IsBackReference => BackReferenceDelta.HasValue;

        internal FixupCellListHandle(int offset, uint? backReferenceDelta)
        {
            Offset = offset;
            BackReferenceDelta = backReferenceDelta;
        }
    }

    /// <summary>
    /// Opaque handle to the start of an <see cref="InstanceMethodEntry"/> payload
    /// (a method signature blob immediately followed by entry-point and fixup data).
    /// Pass to <see cref="ReadyToRunReader.GetInstanceMethodPayload(InstanceMethodEntry)"/> to decode.
    /// The underlying value is the file offset (not RVA) of the payload start.
    /// </summary>
    public enum InstanceMethodPayloadOffset : uint { }

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
