// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Helper to read memory by 4-bit (half-byte) nibbles as is used for encoding
    /// method fixups. More or less ported over from CoreCLR src\inc\nibblestream.h.
    /// </summary>
    internal class NibbleReader
    {
        /// <summary>
        /// Special value in _nextNibble saying there's no next nibble and the next byte
        /// must be read from the image.
        /// </summary>
        private const byte NoNextNibble = 0xFF;

        /// <summary>
        /// NativeReader representing the PE file.
        /// </summary>
        private NativeReader _imageReader;

        /// <summary>
        /// Offset within the image.
        /// </summary>
        private int _offset;
        private readonly int _endOffset;

        /// <summary>
        /// Value of the next nibble or 0xFF when there's no cached next nibble.
        /// </summary>
        private byte _nextNibble;

        public NibbleReader(NativeReader imageReader, int offset)
            : this(
                imageReader,
                offset,
                imageReader.Length > int.MaxValue ? int.MaxValue : (int)imageReader.Length)
        {
        }

        public NibbleReader(NativeReader imageReader, int offset, int endOffset)
        {
            _imageReader = imageReader ?? throw new System.ArgumentNullException(nameof(imageReader));
            if (offset < 0 || offset > endOffset || endOffset > imageReader.Length)
                throw new BadImageFormatException("Nibble stream range is outside the image.");

            _offset = offset;
            _endOffset = endOffset;
            _nextNibble = NoNextNibble;
        }

        public byte ReadNibble()
        {
            byte result;
            if (_nextNibble != NoNextNibble)
            {
                result = _nextNibble;
                _nextNibble = NoNextNibble;
            }
            else
            {
                if (_offset >= _endOffset)
                    throw new BadImageFormatException("Nibble stream extends beyond its containing range.");

                _nextNibble = _imageReader.ReadByte(ref _offset);
                result = (byte)(_nextNibble & 0x0F);
                _nextNibble >>= 4;
            }
            return result;
        }

        /// <summary>
        /// Read an unsigned int that was encoded via variable length nibble encoding
        /// from CoreCLR NibbleWriter::WriteEncodedU32.
        /// </summary>
        public uint ReadUInt()
        {
            uint value = 0;

            // The encoding is variably lengthed, with the high-bit of every nibble indicating whether
            // there is another nibble in the value.  Each nibble contributes 3 bits to the value.
            uint nibble;
            do
            {
                nibble = ReadNibble();
                uint payload = nibble & 0x7;
                if (value > (uint.MaxValue - payload) >> 3)
                    throw new BadImageFormatException("Nibble-encoded unsigned integer overflows UInt32.");

                value = (value << 3) + payload;
            }
            while ((nibble & 0x8) != 0);

            return value;
        }

        /// <summary>
        /// Read an encoded signed integer from the nibble reader. This uses the same unsigned
        /// encoding, just left shifting the absolute value by one and filling in bit #0 with the sign bit.
        /// </summary>
        public int ReadInt()
        {
            uint unsignedValue = ReadUInt();
            int signedValue = (int)(unsignedValue >> 1);
            return ((unsignedValue & 1) != 0 ? -signedValue : signedValue);
        }

        /// <summary>
        ///
        /// </summary>
        public int GetNextByteOffset() => _offset;
    }
}
