// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;

using Internal.Runtime;

namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// A section whose type identifier is defined by the reader's ReadyToRun format model.
    /// </summary>
    public readonly record struct KnownReadyToRunSection
    {
        public KnownReadyToRunSection(ReadyToRunSectionType type, ImageRVA relativeVirtualAddress, int size)
            : this(type, relativeVirtualAddress, size, validate: true)
        {
        }

        private KnownReadyToRunSection(
            ReadyToRunSectionType type,
            ImageRVA relativeVirtualAddress,
            int size,
            bool validate)
        {
            if (validate)
            {
                if (!Enum.IsDefined(type))
                    throw new ArgumentOutOfRangeException(nameof(type), type, "The section type is not known.");
                ArgumentOutOfRangeException.ThrowIfNegative(size);
            }

            Type = type;
            RelativeVirtualAddress = relativeVirtualAddress;
            Size = size;
        }

        internal static KnownReadyToRunSection CreateUnchecked(
            ReadyToRunSectionType type,
            ImageRVA relativeVirtualAddress,
            int size)
            => new(type, relativeVirtualAddress, size, validate: false);

        /// <summary>The known ReadyToRun section type.</summary>
        public ReadyToRunSectionType Type { get; }

        /// <summary>The section's raw type identifier.</summary>
        public int RawType => (int)Type;

        /// <summary>The raw RVA of the section's first byte.</summary>
        public ImageRVA RelativeVirtualAddress { get; }

        /// <summary>The raw RVA value encoded in the section directory.</summary>
        public uint RawRelativeVirtualAddress => (uint)RelativeVirtualAddress;

        /// <summary>The section length.</summary>
        public int Size { get; }

        /// <summary>The raw unsigned length encoded in the section directory.</summary>
        public uint RawSize => (uint)Size;

        public override string ToString()
            => $"{Type}: RVA 0x{RawRelativeVirtualAddress:X8}, Size {RawSize}";
    }

    /// <summary>
    /// A section whose type identifier isn't defined by the reader's ReadyToRun format model.
    /// Unknown sections remain available for raw inspection and forward-compatible traversal.
    /// </summary>
    public readonly record struct UnknownReadyToRunSection
    {
        public UnknownReadyToRunSection(int typeId, ImageRVA relativeVirtualAddress, int size)
        {
            if (Enum.IsDefined((ReadyToRunSectionType)typeId))
                throw new ArgumentOutOfRangeException(nameof(typeId), typeId, "Use KnownReadyToRunSection for a known type.");
            ArgumentOutOfRangeException.ThrowIfNegative(size);

            TypeId = typeId;
            RelativeVirtualAddress = relativeVirtualAddress;
            Size = size;
        }

        /// <summary>The raw section type identifier.</summary>
        public int TypeId { get; }

        /// <summary>The raw RVA of the section's first byte.</summary>
        public ImageRVA RelativeVirtualAddress { get; }

        /// <summary>The raw RVA value encoded in the section directory.</summary>
        public uint RawRelativeVirtualAddress => (uint)RelativeVirtualAddress;

        /// <summary>The section length.</summary>
        public int Size { get; }

        /// <summary>The raw unsigned length encoded in the section directory.</summary>
        public uint RawSize => (uint)Size;

        public override string ToString()
            => $"Section {TypeId}: RVA 0x{RawRelativeVirtualAddress:X8}, Size {RawSize}";
    }

    /// <summary>
    /// A ReadyToRun section directory entry. A value is either a known section or an unknown
    /// section preserved for forward-compatible raw inspection.
    /// </summary>
    /// <remarks>
    /// Crossgen2 emitter: <c>ReadyToRunHeaderNode (each entry corresponds to one section registered via Header.Add)</c>.
    /// </remarks>
    [Union]
    public readonly struct ReadyToRunSection : IUnion, IEquatable<ReadyToRunSection>
    {
        private readonly int _rawType;
        private readonly uint _rawRelativeVirtualAddress;
        private readonly int _size;
        private readonly bool _isKnown;

        public ReadyToRunSection(KnownReadyToRunSection section)
        {
            _rawType = section.RawType;
            _rawRelativeVirtualAddress = section.RawRelativeVirtualAddress;
            _size = section.Size;
            _isKnown = true;
        }

        public ReadyToRunSection(UnknownReadyToRunSection section)
        {
            _rawType = section.TypeId;
            _rawRelativeVirtualAddress = section.RawRelativeVirtualAddress;
            _size = section.Size;
            _isKnown = false;
        }

        internal static ReadyToRunSection Create(int typeId, ImageRVA relativeVirtualAddress, int size)
        {
            ReadyToRunSectionType type = (ReadyToRunSectionType)typeId;
            return Enum.IsDefined(type)
                ? new ReadyToRunSection(new KnownReadyToRunSection(type, relativeVirtualAddress, size))
                : new ReadyToRunSection(new UnknownReadyToRunSection(typeId, relativeVirtualAddress, size));
        }

        /// <summary>The contained known or unknown section value.</summary>
        public object Value => _isKnown
            ? KnownReadyToRunSection.CreateUnchecked(
                (ReadyToRunSectionType)_rawType,
                (ImageRVA)_rawRelativeVirtualAddress,
                _size)
            : new UnknownReadyToRunSection(_rawType, (ImageRVA)_rawRelativeVirtualAddress, _size);

        /// <summary>Always returns true; the default value represents an unknown section with type identifier zero.</summary>
        public bool HasValue => true;

        public bool TryGetValue(out KnownReadyToRunSection section)
        {
            if (_isKnown)
            {
                section = KnownReadyToRunSection.CreateUnchecked(
                    (ReadyToRunSectionType)_rawType,
                    (ImageRVA)_rawRelativeVirtualAddress,
                    _size);
                return true;
            }

            section = default;
            return false;
        }

        public bool TryGetValue(out UnknownReadyToRunSection section)
        {
            if (!_isKnown)
            {
                section = new UnknownReadyToRunSection(
                    _rawType,
                    (ImageRVA)_rawRelativeVirtualAddress,
                    _size);
                return true;
            }

            section = default;
            return false;
        }

        /// <summary>The raw section type identifier exactly as encoded in the directory.</summary>
        public int RawType => _rawType;

        /// <summary>The section's raw RVA.</summary>
        public ImageRVA RelativeVirtualAddress => (ImageRVA)_rawRelativeVirtualAddress;

        /// <summary>The raw RVA exactly as encoded in the section directory.</summary>
        public uint RawRelativeVirtualAddress => _rawRelativeVirtualAddress;

        /// <summary>The section length.</summary>
        public int Size => _size;

        /// <summary>The raw unsigned length encoded in the section directory.</summary>
        public uint RawSize => (uint)_size;

        /// <summary>The section type. Prefer matching <c>KnownReadyToRunSection</c> for new code.</summary>
        [Obsolete("Match the union as KnownReadyToRunSection or inspect RawType instead.")]
        public ReadyToRunSectionType Type => (ReadyToRunSectionType)_rawType;

        public bool Equals(ReadyToRunSection other)
            => _rawType == other._rawType
                && _rawRelativeVirtualAddress == other._rawRelativeVirtualAddress
                && _size == other._size
                && _isKnown == other._isKnown;

        public override bool Equals(object obj) => obj is ReadyToRunSection other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_rawType, _rawRelativeVirtualAddress, _size, _isKnown);

        public static bool operator ==(ReadyToRunSection left, ReadyToRunSection right) => left.Equals(right);

        public static bool operator !=(ReadyToRunSection left, ReadyToRunSection right) => !left.Equals(right);

        public override string ToString()
            => Value.ToString();
    }
}
