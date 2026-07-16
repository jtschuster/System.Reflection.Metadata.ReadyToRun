// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;

namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// based on <a href="https://github.com/dotnet/runtime/blob/main/src/coreclr/vm/nativeformatreader.h">NativeFormat::NativeParser</a>
    /// </summary>
    public struct NativeParser
    {
        // TODO (refactoring) - all these Native* class should be private
        /// <summary>
        /// The current index of the image byte array
        /// </summary>
        public uint Offset { get; set; }
        public byte LowHashcode { get; }

        NativeReader _imageReader;
        uint _endOffset;

        public NativeParser(NativeReader imageReader, uint offset, byte lowHashcode = 0)
            : this(
                imageReader,
                offset,
                imageReader is null || imageReader.Length > uint.MaxValue
                    ? uint.MaxValue
                    : (uint)imageReader.Length,
                lowHashcode)
        {
        }

        internal NativeParser(NativeReader imageReader, uint offset, uint endOffset, byte lowHashcode = 0)
        {
            if (imageReader is null)
                throw new ArgumentNullException(nameof(imageReader));
            if (offset > endOffset || endOffset > imageReader.Length)
                throw new BadImageFormatException("NativeParser range is outside the image.");

            Offset = offset;
            LowHashcode = lowHashcode;
            _imageReader = imageReader;
            _endOffset = endOffset;
        }

        public bool IsNull()
        {
            return _imageReader == null;
        }

        public uint GetRelativeOffset()
        {
            uint pos = Offset;

            int delta = 0;
            Offset = _imageReader.DecodeSigned(Offset, ref delta);
            EnsureWithinBounds();

            return pos + (uint)delta;
        }

        public NativeParser GetParserFromRelativeOffset()
        {
            byte lowHashcode = GetByte();
            return new NativeParser(_imageReader, GetRelativeOffset(), _endOffset, lowHashcode);
        }

        public byte GetByte()
        {
            if (Offset >= _endOffset)
                throw new BadImageFormatException("NativeParser read extends beyond its containing range.");

            int off = (int)Offset;
            byte val = _imageReader.ReadByte(ref off);
            Offset += 1;
            return val;
        }

        public uint GetCompressedData()
        {
            int off = (int)Offset;
            uint val = _imageReader.ReadCompressedData(ref off);
            Offset = (uint)off;
            EnsureWithinBounds();
            return val;
        }

        public uint GetUnsigned()
        {
            uint value = 0;
            Offset = _imageReader.DecodeUnsigned(Offset, ref value);
            EnsureWithinBounds();
            return value;
        }

        public int GetSigned()
        {
            int value = 0;
            Offset = _imageReader.DecodeSigned(Offset, ref value);
            EnsureWithinBounds();
            return value;
        }

        private void EnsureWithinBounds()
        {
            if (Offset > _endOffset)
                throw new BadImageFormatException("NativeParser read extends beyond its containing range.");
        }
    }

    /// <summary>
    /// based on <a href="https://github.com/dotnet/runtime/blob/main/src/coreclr/vm/nativeformatreader.h">NativeFormat::NativeHashtable</a>
    /// </summary>
    public struct NativeHashtable
    {
        // TODO (refactoring) - all these Native* class should be private
        private NativeReader _imageReader;
        private uint _baseOffset;
        private uint _bucketMask;
        private byte _entryIndexSize;
        private uint _endOffset;
        private uint _startOffset;
        private uint _entryIndexByteCount;

        public NativeHashtable(NativeReader imageReader, NativeParser parser, uint endOffset)
        {
            _imageReader = imageReader ?? throw new ArgumentNullException(nameof(imageReader));
            if (parser.IsNull() || parser.Offset >= endOffset || endOffset > imageReader.Length)
                throw new BadImageFormatException("NativeHashtable range is outside the image.");

            parser = new NativeParser(imageReader, parser.Offset, endOffset, parser.LowHashcode);
            _startOffset = parser.Offset;
            uint header = parser.GetByte();
            _baseOffset = parser.Offset;

            int numberOfBucketsShift = (int)(header >> 2);
            if (numberOfBucketsShift > 31)
                throw new BadImageFormatException("NativeHashtable has an invalid bucket count.");
            uint numberOfBuckets = 1u << numberOfBucketsShift;
            _bucketMask = numberOfBuckets - 1;

            byte entryIndexSize = (byte)(header & 3);
            if (entryIndexSize > 2)
                throw new BadImageFormatException("NativeHashtable has an invalid entry index size.");
            _entryIndexSize = entryIndexSize;

            ulong entryIndexByteCount = ((ulong)numberOfBuckets + 1) << _entryIndexSize;
            if (entryIndexByteCount > uint.MaxValue
                || (ulong)_baseOffset + entryIndexByteCount > endOffset)
            {
                throw new BadImageFormatException("NativeHashtable bucket index extends beyond its containing range.");
            }

            _entryIndexByteCount = (uint)entryIndexByteCount;
            _endOffset = endOffset;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            SortedDictionary<uint, byte> entries = new SortedDictionary<uint, byte>();
            AllEntriesEnumerator allEntriesEnum = EnumerateAllEntries();
            NativeParser curParser = allEntriesEnum.GetNext();
            while (!curParser.IsNull())
            {
                entries[curParser.Offset] = curParser.LowHashcode;
                curParser = allEntriesEnum.GetNext();
            }
            entries[_endOffset] = 0;

            sb.AppendLine($"NativeHashtable Size: {entries.Count - 1}");
            sb.AppendLine($"EntryIndexSize: {_entryIndexSize}");
            int curOffset = -1;
            foreach (KeyValuePair<uint, byte> entry in entries)
            {
                int nextOffset = (int)entry.Key;
                if (curOffset != -1)
                {
                    for (int i = curOffset; i < nextOffset; i++)
                    {
                        sb.Append($"{_imageReader[i]:X2} ");
                    }
                    sb.AppendLine();
                }
                if (nextOffset != _endOffset)
                {
                    sb.Append($"0x{entry.Value:X2} -> ");
                }
                curOffset = nextOffset;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Enumerates candidate matches for a hashcode lookup within a <em>single</em> hash bucket.
        /// <see cref="NativeHashtable.Lookup(int)"/> selects the bucket from the upper bits of the
        /// hashcode, and this enumerator walks that one bucket looking for entries whose low
        /// hashcode byte matches the lookup target. Bucket entries are sorted by low hashcode,
        /// so the scan terminates as soon as it passes the target.
        ///
        /// Multiple matches per bucket are normal because the low byte is only an 8-bit probe
        /// filter; the caller must verify the full key against each entry returned by
        /// <see cref="GetNext"/>.
        ///
        /// The enumerator does not conform to the regular C# enumerator pattern to avoid paying
        /// its performance penalty (allocation, multiple calls per iteration).
        /// </summary>
        public struct Enumerator
        {
            private NativeParser _parser;
            private NativeHashtable _table;
            private uint _endOffset;
            private byte _lowHashcode;

            internal Enumerator(NativeHashtable table, NativeParser parser, uint endOffset, byte lowHashcode)
            {
                _table = table;
                _parser = parser;
                _endOffset = endOffset;
                _lowHashcode = lowHashcode;
            }

            /// <summary>
            /// Advance to the next bucket entry whose low hashcode byte matches the looked-up
            /// hashcode. Returns a parser positioned at that entry's payload, or a null parser
            /// when no more matches are available. Multiple matches are possible (low-byte hash
            /// collisions within the bucket); the caller must verify the full key against each
            /// returned entry.
            /// </summary>
            public NativeParser GetNext()
            {
                while (_parser.Offset < _endOffset)
                {
                    uint entryStart = _parser.Offset;
                    byte lowHashcode = _parser.GetByte();

                    if (lowHashcode == _lowHashcode)
                    {
                        // Rewind so GetParserFromRelativeOffset re-reads the byte and the
                        // signed delta, returning a parser at the entry's payload.
                        _parser.Offset = entryStart;
                        return _table.GetEntryParser(ref _parser, _endOffset);
                    }

                    // The entries are sorted by low hashcode within the bucket, so once we
                    // see a higher value we can terminate the scan early.
                    if (lowHashcode > _lowHashcode)
                    {
                        _endOffset = _parser.Offset;
                        break;
                    }

                    // Skip past the signed relative-offset integer for this non-matching entry.
                    _parser.GetSigned();
                    if (_parser.Offset > _endOffset)
                        throw new BadImageFormatException("NativeHashtable entry extends beyond its bucket.");
                }
                return default;
            }
        }

        public struct AllEntriesEnumerator
        {
            private NativeHashtable _table;
            private NativeParser _parser;
            private uint _currentBucket;
            private uint _endOffset;

            internal AllEntriesEnumerator(NativeHashtable table)
            {
                _table = table;
                _currentBucket = 0;
                _parser = _table.GetParserForBucket(_currentBucket, out _endOffset);
            }

            public NativeParser GetNext()
            {
                for (; ; )
                {
                    while (_parser.Offset < _endOffset)
                    {
                        return _table.GetEntryParser(ref _parser, _endOffset);
                    }

                    if (_currentBucket >= _table._bucketMask)
                        return new NativeParser();

                    _currentBucket++;
                    _parser = _table.GetParserForBucket(_currentBucket, out _endOffset);
                }
            }
        }

        private NativeParser GetParserForBucket(uint bucket, out uint endOffset)
        {
            if (bucket > _bucketMask)
                throw new BadImageFormatException("NativeHashtable bucket index is out of bounds.");

            uint start, end;

            if (_entryIndexSize == 0)
            {
                int bucketOffset = (int)(_baseOffset + bucket);
                start = _imageReader.ReadByte(ref bucketOffset);
                end = _imageReader.ReadByte(ref bucketOffset);
            }
            else if (_entryIndexSize == 1)
            {
                int bucketOffset = (int)(_baseOffset + 2 * bucket);
                start = _imageReader.ReadUInt16(ref bucketOffset);
                end = _imageReader.ReadUInt16(ref bucketOffset);
            }
            else
            {
                int bucketOffset = (int)(_baseOffset + 4 * bucket);
                start = _imageReader.ReadUInt32(ref bucketOffset);
                end = _imageReader.ReadUInt32(ref bucketOffset);
            }

            if (start > end || start < _entryIndexByteCount)
                throw new BadImageFormatException("NativeHashtable bucket offsets are invalid.");

            ulong absoluteStart = (ulong)_baseOffset + start;
            ulong absoluteEnd = (ulong)_baseOffset + end;
            if (absoluteStart > absoluteEnd || absoluteEnd > _endOffset)
                throw new BadImageFormatException("NativeHashtable bucket extends beyond its containing range.");

            endOffset = (uint)absoluteEnd;
            return new NativeParser(_imageReader, (uint)absoluteStart, _endOffset);
        }

        private NativeParser GetEntryParser(ref NativeParser parser, uint bucketEndOffset)
        {
            NativeParser entryParser = parser.GetParserFromRelativeOffset();
            if (parser.Offset > bucketEndOffset)
                throw new BadImageFormatException("NativeHashtable entry extends beyond its bucket.");
            if (entryParser.Offset < _startOffset || entryParser.Offset >= _endOffset)
                throw new BadImageFormatException("NativeHashtable entry payload offset is out of bounds.");

            return entryParser;
        }

        /// <summary>
        /// Begin a hashcode lookup. Selects the single hash bucket addressed by the upper bits
        /// of <paramref name="hashcode"/> (<c>(hashcode &gt;&gt; 8) &amp; bucketMask</c>) and
        /// returns an <see cref="Enumerator"/> that walks <em>only that bucket</em>, yielding
        /// entries whose stored low hashcode byte equals <c>(byte)hashcode</c>.
        ///
        /// Because the low byte is just an 8-bit probe filter, multiple matches may be returned
        /// from the same bucket; the caller is responsible for verifying the full key against
        /// each candidate. Entries whose full hashcode falls in a different bucket are never
        /// considered.
        /// </summary>
        public Enumerator Lookup(int hashcode)
        {
            uint endOffset;
            uint bucket = ((uint)hashcode >> 8) & _bucketMask;
            NativeParser parser = GetParserForBucket(bucket, out endOffset);

            return new Enumerator(this, parser, endOffset, (byte)hashcode);
        }

        public AllEntriesEnumerator EnumerateAllEntries()
        {
            return new AllEntriesEnumerator(this);
        }
    }

    /// <summary>
    /// based on <a href="https://github.com/dotnet/runtime/blob/main/src/coreclr/vm/nativeformatreader.h">NativeFormat::NativeHashtable</a>
    /// </summary>
    public struct NativeCuckooFilter
    {
        // TODO (refactoring) - all these Native* class should be private
        private NativeReader _imageReader;
        private int _filterStartOffset;
        private int _filterEndOffset;

        public NativeCuckooFilter(NativeReader imageReader, int filterStartOffset, int filterEndOffset)
        {
            _imageReader = imageReader;
            _filterStartOffset = filterStartOffset;
            _filterEndOffset = filterEndOffset;

            if (((_filterStartOffset & 0xF) != 0) || ((_filterEndOffset & 0xF) != 0))
            {
                // Native cuckoo filters must be aligned at 16byte boundaries within the PE file
                throw new System.BadImageFormatException();
            }
        }

        /// <summary>Number of 16-byte buckets in the filter (each holds 8 ushort fingerprints).</summary>
        public int BucketCount => (_filterEndOffset - _filterStartOffset) / 16;

        /// <summary>
        /// Enumerates all buckets. Each bucket is an array of 8 little-endian fingerprints.
        /// </summary>
        public IEnumerable<ushort[]> GetBuckets()
        {
            int offset = _filterStartOffset;
            while (offset < _filterEndOffset)
            {
                ushort[] bucket = new ushort[8];
                for (int i = 0; i < bucket.Length; i++)
                {
                    bucket[i] = _imageReader.ReadUInt16(ref offset);
                }
                yield return bucket;
            }
        }

        /// <summary>
        /// Returns true when the filter <em>may</em> contain an entry with the given
        /// <paramref name="hashcode"/> and <paramref name="fingerprint"/>.
        /// False positives are possible; false negatives are not.
        /// An empty filter (BucketCount == 0) always returns false.
        /// </summary>
        /// <remarks>
        /// Algorithm mirrors <c>NativeCuckooFilter::MayExist</c> in
        /// <c>src/coreclr/vm/nativeformatreader.h</c>.
        /// ComputeFingerprintHash is identity (fingerprint == its own hash).
        /// </remarks>
        public bool MayContain(uint hashcode, ushort fingerprint)
        {
            if (fingerprint == 0)
                fingerprint = 1; // fingerprints of 0 are not stored; use 1

            int bucketCount = BucketCount;
            if (bucketCount == 0)
                return false; // empty table — no attributes exist

            uint bucketMask = (uint)(bucketCount - 1); // count is a power of two
            uint bucketAIndex = hashcode & bucketMask;
            uint bucketBIndex = bucketAIndex ^ (fingerprint & bucketMask);

            // Check bucket A
            int offsetA = _filterStartOffset + (int)(bucketAIndex * 16);
            for (int i = 0; i < 8; i++)
            {
                int off = offsetA + i * 2;
                if (_imageReader.ReadUInt16(ref off) == fingerprint)
                    return true;
            }

            // Check bucket B
            int offsetB = _filterStartOffset + (int)(bucketBIndex * 16);
            for (int i = 0; i < 8; i++)
            {
                int off = offsetB + i * 2;
                if (_imageReader.ReadUInt16(ref off) == fingerprint)
                    return true;
            }

            return false;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"NativeCuckooFilter Size: {(_filterEndOffset - _filterStartOffset) / 16}");
            int bucket = 0;
            foreach (ushort[] bucketContents in GetBuckets())
            {
                sb.Append($"Bucket: {bucket} [");
                for (int i = 0; i < 8; i++)
                {
                    sb.Append($"{bucketContents[i],4:X} ");
                }
                sb.AppendLine("]");
                bucket++;
            }

            return sb.ToString();
        }
    }
}
