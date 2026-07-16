// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Text;

namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// based on <a href="https://github.com/dotnet/runtime/blob/main/src/coreclr/vm/nativeformatreader.h">NativeFormat::NativeArray</a>
    /// </summary>
    public class NativeArray
    {
        private const int _blockSize = 16;

        private NativeReader _reader;
        private uint _baseOffset;
        private uint _nElements;
        private byte _entryIndexSize;
        private uint _endOffset;
        private uint _entryIndexByteCount;

        public NativeArray(NativeReader reader, uint offset)
            : this(
                reader,
                offset,
                reader.Length > uint.MaxValue ? uint.MaxValue : (uint)reader.Length)
        {
        }

        public NativeArray(NativeReader reader, uint offset, uint endOffset)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            if (offset >= endOffset || endOffset > reader.Length)
                throw new BadImageFormatException("NativeArray range is outside the image.");

            uint val = 0;
            _baseOffset = _reader.DecodeUnsigned(offset, ref val);
            if (_baseOffset > endOffset)
                throw new BadImageFormatException("NativeArray header extends beyond its containing range.");

            _nElements = (val >> 2);
            _entryIndexSize = (byte)(val & 3);
            if (_entryIndexSize > 2)
                throw new BadImageFormatException("NativeArray has an invalid entry index size.");

            ulong blockCount = ((ulong)_nElements + _blockSize - 1) / _blockSize;
            ulong entryIndexByteCount = blockCount << _entryIndexSize;
            if (entryIndexByteCount > uint.MaxValue
                || (ulong)_baseOffset + entryIndexByteCount > endOffset)
            {
                throw new BadImageFormatException("NativeArray entry index extends beyond its containing range.");
            }

            _entryIndexByteCount = (uint)entryIndexByteCount;
            _endOffset = endOffset;
        }

        public uint GetCount()
        {
            return _nElements;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"NativeArray Size: {_nElements}");
            sb.AppendLine($"EntryIndexSize: {_entryIndexSize}");
            for (uint i = 0; i < _nElements; i++)
            {
                int val = 0;
                if (TryGetAt(i, ref val))
                {
                    sb.AppendLine($"{i}: {val}");
                }
            }

            return sb.ToString();
        }

        public bool TryGetAt(uint index, ref int pOffset)
        {
            if (index >= _nElements)
                return false;

            uint offset;
            ulong entryIndexOffset = (ulong)_baseOffset
                + ((ulong)(index / _blockSize) << _entryIndexSize);
            if (entryIndexOffset + (1u << _entryIndexSize) > (ulong)_baseOffset + _entryIndexByteCount)
                throw new BadImageFormatException("NativeArray entry index is out of bounds.");

            if (_entryIndexSize == 0)
            {
                int i = checked((int)entryIndexOffset);
                offset = _reader.ReadByte(ref i);
            }
            else if (_entryIndexSize == 1)
            {
                int i = checked((int)entryIndexOffset);
                offset = _reader.ReadUInt16(ref i);
            }
            else
            {
                int i = checked((int)entryIndexOffset);
                offset = _reader.ReadUInt32(ref i);
            }

            ulong absoluteOffset = (ulong)_baseOffset + offset;
            if (absoluteOffset >= _endOffset)
                throw new BadImageFormatException("NativeArray node offset is out of bounds.");
            offset = (uint)absoluteOffset;

            for (uint bit = _blockSize >> 1; bit > 0; bit >>= 1)
            {
                uint val = 0;
                uint offset2 = _reader.DecodeUnsigned(offset, ref val);
                if (offset2 > _endOffset)
                    throw new BadImageFormatException("NativeArray node extends beyond its containing range.");

                if ((index & bit) != 0)
                {
                    if ((val & 2) != 0)
                    {
                        ulong nextOffset = (ulong)offset + (val >> 2);
                        if (nextOffset >= _endOffset)
                            throw new BadImageFormatException("NativeArray branch offset is out of bounds.");
                        offset = (uint)nextOffset;
                        continue;
                    }
                }
                else
                {
                    if ((val & 1) != 0)
                    {
                        offset = offset2;
                        continue;
                    }
                }

                // Not found
                if ((val & 3) == 0)
                {
                    // Matching special leaf node?
                    if ((val >> 2) == (index & (_blockSize - 1)))
                    {
                        offset = offset2;
                        break;
                    }
                }
                return false;
            }
            if (offset >= _endOffset)
                throw new BadImageFormatException("NativeArray payload offset is out of bounds.");

            pOffset = (int)offset;
            return true;
        }
    }
}
