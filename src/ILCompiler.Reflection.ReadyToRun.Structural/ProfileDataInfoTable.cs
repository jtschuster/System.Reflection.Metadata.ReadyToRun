// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;


namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Structural projection of the ProfileDataInfo section (section 111, added in R2R v2.2).
    /// Contains legacy IBC (Instrumentation-Based Code profiling) method block-count records,
    /// linked by relocated image virtual addresses.
    /// </summary>
    /// <remarks>
    /// The section is a singly-linked list. Each record begins with a pointer-sized
    /// <em>next-handle</em> field (a relocated VA), followed by CORBBTPROF header fields
    /// and raw payload bytes. This format is legacy and rarely present in modern R2R images.
    /// </remarks>
    public sealed class ProfileDataInfoTable
    {
        /// <summary>All decoded records, in linked-list traversal order.</summary>
        public IReadOnlyList<ProfileDataInfoEntry> Entries { get; }

        internal ProfileDataInfoTable(IReadOnlyList<ProfileDataInfoEntry> entries)
        {
            Entries = entries;
        }
    }

    /// <summary>
    /// A single IBC profile-data record from the ProfileDataInfo section.
    /// Fields are exactly those sequentially encoded in the image: the next-record VA,
    /// followed by the CORBBTPROF_METHOD_HEADER fields, followed by payload bytes.
    /// </summary>
    public sealed class ProfileDataInfoEntry
    {
        /// <summary>
        /// Relocated image virtual address of the next record in the linked list,
        /// or zero if this is the last record.
        /// This is the raw value as stored in the (un-relocated) PE file.
        /// </summary>
        public ulong NextHandle { get; }

        /// <summary>Total byte size of this record (includes the next-handle and header).</summary>
        public uint Size { get; }

        /// <summary>Number of detail entries (cDetail field).</summary>
        public uint Detail { get; }

        /// <summary>MethodDef token of the profiled method.</summary>
        public uint MethodToken { get; }

        /// <summary>IL size of the profiled method in bytes.</summary>
        public uint ILSize { get; }

        /// <summary>Number of basic-block counter entries (cBlock field).</summary>
        public uint BlockCount { get; }

        /// <summary>
        /// Raw payload bytes following the CORBBTPROF header.
        /// For a well-formed record, this contains <see cref="BlockCount"/> * 8 bytes
        /// of (ILOffset: uint32, ExecutionCount: uint32) pairs.
        /// </summary>
        public byte[] PayloadBytes { get; }

        internal ProfileDataInfoEntry(
            ulong nextHandle,
            uint size,
            uint detail,
            uint methodToken,
            uint ilSize,
            uint blockCount,
            byte[] payloadBytes)
        {
            NextHandle = nextHandle;
            Size = size;
            Detail = detail;
            MethodToken = methodToken;
            ILSize = ilSize;
            BlockCount = blockCount;
            PayloadBytes = payloadBytes;
        }
    }

    public partial class ReadyToRunReader
    {
        /// <summary>
        /// Parses the ProfileDataInfo section as a linked list of legacy IBC method records.
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// Thrown when the underlying image reader does not support VA-to-offset conversion
        /// (e.g. Webcil/WASM images).
        /// </exception>
        /// <exception cref="BadImageFormatException">
        /// Thrown when the linked list is malformed (cycle detected, out-of-bounds pointer,
        /// or truncated record).
        /// </exception>
        public ProfileDataInfoTable GetProfileDataInfoTable(ReadyToRunSection section)
        {
            if (section.Size == 0)
                return new ProfileDataInfoTable(Array.Empty<ProfileDataInfoEntry>());

            int pointerSize = TargetPointerSize;
            int firstOffset = GetOffsetForRVA(section.RelativeVirtualAddress);
            var entries = new List<ProfileDataInfoEntry>();

            // Track visited file offsets to detect cycles.
            var visited = new HashSet<int>();

            int currentOffset = firstOffset;

            while (currentOffset != 0)
            {
                if (!visited.Add(currentOffset))
                    throw new BadImageFormatException(
                        $"ProfileDataInfo linked list contains a cycle at file offset 0x{currentOffset:X}.");

                // Read the pointer-sized next-handle (a relocated image VA stored as 4 or 8 bytes).
                ulong nextHandle;
                if (pointerSize == 8)
                {
                    nextHandle = (ulong)_nativeReader.ReadInt64(ref currentOffset);
                }
                else
                {
                    nextHandle = _nativeReader.ReadUInt32(ref currentOffset);
                }

                // Read CORBBTPROF_METHOD_HEADER fields.
                uint size = _nativeReader.ReadUInt32(ref currentOffset);
                uint detail = _nativeReader.ReadUInt32(ref currentOffset);
                uint methodToken = _nativeReader.ReadUInt32(ref currentOffset);
                uint ilSize = _nativeReader.ReadUInt32(ref currentOffset);
                uint blockCount = _nativeReader.ReadUInt32(ref currentOffset);

                // Read the remaining payload bytes.
                int headerByteCount = pointerSize + 5 * sizeof(uint);
                int payloadByteCount = (int)size > headerByteCount ? (int)size - headerByteCount : 0;
                byte[] payload = new byte[payloadByteCount];
                for (int i = 0; i < payloadByteCount; i++)
                    payload[i] = _nativeReader.ReadByte(ref currentOffset);

                entries.Add(new ProfileDataInfoEntry(nextHandle, size, detail, methodToken, ilSize, blockCount, payload));

                // Follow the next-handle pointer to the next record.
                if (nextHandle == 0)
                    break;

                if (!_platformBinaryReader.TryGetFileOffsetFromImageVA((long)nextHandle, out int nextFileOffset))
                    throw new BadImageFormatException(
                        $"ProfileDataInfo next handle 0x{nextHandle:X} could not be converted to a file offset. " +
                        "The platform binary reader may not support VA-to-offset conversion.");

                currentOffset = nextFileOffset;
            }

            return new ProfileDataInfoTable(entries);
        }
    }
}
