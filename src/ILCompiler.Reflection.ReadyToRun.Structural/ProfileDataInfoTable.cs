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

        /// <summary>
        /// Byte size of the CORBBTPROF method header and payload.
        /// The preceding next-handle pointer is not included.
        /// </summary>
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
            int sectionOffset = ValidateAndGetSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.ProfileDataInfo,
                nameof(GetProfileDataInfoTable));
            if (section.Size == 0)
                return new ProfileDataInfoTable(Array.Empty<ProfileDataInfoEntry>());

            int pointerSize = TargetPointerSize;
            int sectionEndOffset = checked(sectionOffset + section.Size);
            var entries = new List<ProfileDataInfoEntry>();
            var visited = new HashSet<int>();
            int currentOffset = sectionOffset;

            while (true)
            {
                if (currentOffset < sectionOffset || currentOffset >= sectionEndOffset)
                    throw new BadImageFormatException("ProfileDataInfo record starts outside its containing section.");
                if (!visited.Add(currentOffset))
                    throw new BadImageFormatException(
                        $"ProfileDataInfo linked list contains a cycle at file offset 0x{currentOffset:X}.");
                int recordOffset = currentOffset;
                const int methodHeaderByteCount = 5 * sizeof(uint);
                if (sectionEndOffset - recordOffset < pointerSize + methodHeaderByteCount)
                    throw new BadImageFormatException("ProfileDataInfo record header is truncated.");

                ulong nextHandle;
                if (pointerSize == 8)
                {
                    nextHandle = (ulong)_nativeReader.ReadInt64(ref currentOffset);
                }
                else
                {
                    nextHandle = _nativeReader.ReadUInt32(ref currentOffset);
                }

                uint size = _nativeReader.ReadUInt32(ref currentOffset);
                uint detail = _nativeReader.ReadUInt32(ref currentOffset);
                uint methodToken = _nativeReader.ReadUInt32(ref currentOffset);
                uint ilSize = _nativeReader.ReadUInt32(ref currentOffset);
                uint blockCount = _nativeReader.ReadUInt32(ref currentOffset);

                if (size < methodHeaderByteCount)
                    throw new BadImageFormatException("ProfileDataInfo record size is smaller than its method header.");

                uint payloadByteCount = size - methodHeaderByteCount;
                long recordEndOffset = (long)recordOffset + pointerSize + size;
                if (recordEndOffset > sectionEndOffset)
                    throw new BadImageFormatException("ProfileDataInfo record extends beyond its containing section.");

                byte[] payload = new byte[payloadByteCount];
                _nativeReader.ReadSpanAt(ref currentOffset, payload);

                entries.Add(new ProfileDataInfoEntry(nextHandle, size, detail, methodToken, ilSize, blockCount, payload));

                if (nextHandle == 0)
                    break;

                if (nextHandle > long.MaxValue)
                    throw new BadImageFormatException($"ProfileDataInfo next handle 0x{nextHandle:X} is outside the supported VA range.");
                if (!_platformBinaryReader.TryGetFileOffsetFromImageVA((long)nextHandle, out int nextFileOffset))
                    throw new BadImageFormatException(
                        $"ProfileDataInfo next handle 0x{nextHandle:X} could not be converted to a file offset.");
                if (nextFileOffset < sectionOffset || nextFileOffset >= sectionEndOffset)
                    throw new BadImageFormatException("ProfileDataInfo next handle points outside its containing section.");

                currentOffset = nextFileOffset;
            }

            return new ProfileDataInfoTable(entries);
        }
    }
}
