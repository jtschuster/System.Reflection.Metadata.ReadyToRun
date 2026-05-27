// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;


namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Structural projection of the InstanceMethodEntryPoints section.
    /// A NativeHashtable where each entry contains a signature blob offset,
    /// runtime function index, and optional fixup cells.
    /// No signature decoding is performed.
    /// </summary>
    /// <remarks>
    /// Crossgen2 emitter: <c>InstanceEntryPointTableNode</c>.
    /// </remarks>
    public sealed class InstanceMethodEntryPointsTable
    {
        public IReadOnlyList<InstanceMethodEntry> Entries { get; }

        internal InstanceMethodEntryPointsTable(List<InstanceMethodEntry> entries)
        {
            Entries = entries;
        }
    }

    public partial class ReadyToRunReader
    {
        public InstanceMethodEntryPointsTable GetInstanceMethodEntryPointsTable(ReadyToRunSection section)
        {
            int sectionOffset = GetOffsetForRVA(section.RelativeVirtualAddress);
            NativeParser parser = new NativeParser(_nativeReader, (uint)sectionOffset);
            NativeHashtable hashtable = new NativeHashtable(_nativeReader, parser, (uint)(sectionOffset + section.Size));
            NativeHashtable.AllEntriesEnumerator enumerator = hashtable.EnumerateAllEntries();
            var entries = new List<InstanceMethodEntry>();

            NativeParser curParser = enumerator.GetNext();
            while (!curParser.IsNull())
            {
                int signatureBlobOffset = (int)curParser.Offset;
                byte lowHashcode = curParser.LowHashcode;

                entries.Add(new InstanceMethodEntry(signatureBlobOffset, lowHashcode));
                curParser = enumerator.GetNext();
            }

            return new InstanceMethodEntryPointsTable(entries);
        }

        /// <summary>
        /// Fully parse an <see cref="InstanceMethodEntry"/>: decode the method signature,
        /// followed by the inline runtime-function-index and optional fixup-list handle. The payload
        /// layout is method-signature || DecodeUnsigned(id) || optional back-reference.
        /// </summary>
        public InstanceMethodPayload GetInstanceMethodPayload(InstanceMethodEntry entry)
        {
            R2RSignatureDecodeResult signature = RawSignatureDecoder.DecodeMethodSignatureWithEndOffset(_nativeReader, entry.SignatureBlobOffset, TargetPointerSize);

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

        public InstanceMethodPayload(R2RSignature methodSignature, RuntimeFunctionIndex entryPointIndex, FixupCellListHandle? fixupCellListHandle)
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
    /// A single entry in the InstanceMethodEntryPoints hashtable.
    /// Contains the offset of the signature blob for this generic method instantiation.
    /// The signature must be decoded by a higher-level reader to extract the
    /// runtime function index and fixup cells.
    /// </summary>
    public sealed class InstanceMethodEntry
    {
        /// <summary>File offset of the method signature blob.</summary>
        public int SignatureBlobOffset { get; }

        /// <summary>Low byte of the hash code used for hashtable bucketing.</summary>
        public byte LowHashcode { get; }

        public InstanceMethodEntry(int signatureBlobOffset, byte lowHashcode)
        {
            SignatureBlobOffset = signatureBlobOffset;
            LowHashcode = lowHashcode;
        }
    }
}
