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
        /// <summary>
        /// Open the InstanceMethodEntryPoints section as an inert table that can be used for
        /// keyed lookups or full enumeration without eagerly decoding every entry.
        /// </summary>
        public InstanceMethodEntryPointsTable GetInstanceMethodEntryPointsTable(ReadyToRunSection section)
        {
            if (section.Type != Internal.Runtime.ReadyToRunSectionType.InstanceMethodEntryPoints)
                throw new InvalidOperationException();

            return new InstanceMethodEntryPointsTable(section.RelativeVirtualAddress, section.Size);
        }

        private NativeHashtable OpenInstanceMethodEntryPointsHashtable(InstanceMethodEntryPointsTable table)
        {
            int sectionOffset = GetOffsetForRVA(table.SectionRva);
            NativeParser parser = new NativeParser(_nativeReader, (uint)sectionOffset);
            return new NativeHashtable(_nativeReader, parser, (uint)(sectionOffset + table.SectionSize));
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
            NativeHashtable hashtable = OpenInstanceMethodEntryPointsHashtable(table);
            NativeHashtable.Enumerator enumerator = hashtable.Lookup(versionResilientHash);

            NativeParser entryParser = enumerator.GetNext();
            while (!entryParser.IsNull())
            {
                int payloadOffset = (int)entryParser.Offset;
                R2RSignatureDecodeResult signature = RawSignatureDecoder.DecodeMethodSignatureWithEndOffset(_nativeReader, payloadOffset, TargetPointerSize, options);
                MethodSignature methodSig = MethodSignature.FromSignature(signature.Signature);
                if (predicate(methodSig))
                {
                    (RuntimeFunctionIndex runtimeFunctionIndex, FixupCellListHandle? fixupCellListHandle) = DecodeRuntimeFunctionIdAndFixupCellList(signature.EndOffset);
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
            NativeHashtable hashtable = OpenInstanceMethodEntryPointsHashtable(table);
            NativeHashtable.AllEntriesEnumerator enumerator = hashtable.EnumerateAllEntries();

            for (NativeParser curParser = enumerator.GetNext(); !curParser.IsNull(); curParser = enumerator.GetNext())
            {
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
            R2RSignatureDecodeResult signature = RawSignatureDecoder.DecodeMethodSignatureWithEndOffset(_nativeReader, (int)entry.PayloadOffset, TargetPointerSize, options);

            int offset = signature.EndOffset;
            (RuntimeFunctionIndex runtimeFunctionIndex, FixupCellListHandle? fixupCellListHandle) = DecodeRuntimeFunctionIdAndFixupCellList(offset);
            return new InstanceMethodPayload(signature.Signature, runtimeFunctionIndex, fixupCellListHandle);
        }

        /// <summary>
        /// Shared decode for MethodDefEntry/InstanceMethodEntry payload tail:
        /// compressed "id" (bit 0 = has-fixups, bit 1 = back-reference), followed by optional fixup list data.
        /// </summary>
        internal (RuntimeFunctionIndex, FixupCellListHandle?) DecodeRuntimeFunctionIdAndFixupCellList(int offset)
        {
            uint id = 0;
            offset = (int)_nativeReader.DecodeUnsigned((uint)offset, ref id);

            FixupCellListHandle? fixupCells = null;
            RuntimeFunctionIndex runtimeFunctionIndex;

            if ((id & 1) != 0)
            {
                uint? backReferenceDelta = null;
                int fixupOffset = offset;

                if ((id & 2) != 0)
                {
                    uint delta = 0;
                    _nativeReader.DecodeUnsigned((uint)offset, ref delta);
                    backReferenceDelta = delta;
                    fixupOffset = checked(offset - (int)delta);
                }

                fixupCells = new FixupCellListHandle(fixupOffset, backReferenceDelta);
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
            var fixupCells = new List<FixupCellRef>();

            NibbleReader nibbleReader = new NibbleReader(_nativeReader, fixupCellList.Offset);
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

                    cellIndex += delta;
                }

                uint tableDelta = nibbleReader.ReadUInt();
                if (tableDelta == 0)
                    break;

                curTableIndex += tableDelta;
            }

            return fixupCells;
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
