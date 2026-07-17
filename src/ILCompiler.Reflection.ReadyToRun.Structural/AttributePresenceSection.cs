// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;


namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Structural projection of the AttributePresence section (section 113).
    /// Wraps a <see cref="NativeCuckooFilter"/> that probabilistically records which custom
    /// attributes are present in the module.
    /// </summary>
    /// <remarks>
    /// Layout: section RVA and size must be 16-byte aligned; size must be zero or a power of two.
    /// Each 16-byte bucket holds 8 little-endian ushort fingerprints.
    /// Crossgen2 emitter: <c>AttributePresenceFilterNode</c>.
    /// Runtime consumer: <c>NativeCuckooFilter::MayExist</c> in
    /// <c>src/coreclr/vm/nativeformatreader.h</c>.
    /// </remarks>
    public sealed class AttributePresenceSection
    {
        private readonly NativeCuckooFilter _filter;

        /// <summary>Number of 16-byte buckets in the filter.</summary>
        public int BucketCount => _filter.BucketCount;

        internal AttributePresenceSection(NativeCuckooFilter filter)
        {
            _filter = filter;
        }

        /// <summary>
        /// Enumerates all filter buckets. Each bucket contains 8 little-endian ushort fingerprints.
        /// </summary>
        public IEnumerable<ushort[]> GetBuckets() => _filter.GetBuckets();

        /// <summary>
        /// Returns true when the filter <em>may</em> contain an entry with the given
        /// <paramref name="hashcode"/> and <paramref name="fingerprint"/>.
        /// False positives are possible; false negatives are not.
        /// An empty filter (BucketCount == 0) always returns false.
        /// </summary>
        /// <remarks>
        /// This is a probabilistic query only. A true result does not guarantee presence.
        /// Algorithm mirrors <c>NativeCuckooFilter::MayExist</c> in the runtime.
        /// </remarks>
        public bool MayContain(uint hashcode, ushort fingerprint) => _filter.MayContain(hashcode, fingerprint);
    }

    public partial class ReadyToRunReader
    {
        /// <summary>
        /// Parses the AttributePresence section as a <see cref="NativeCuckooFilter"/>.
        /// </summary>
        /// <exception cref="BadImageFormatException">
        /// Thrown when the section is not properly aligned (16-byte boundary) or
        /// when its size is neither zero nor a power of two.
        /// </exception>
        public AttributePresenceSection GetAttributePresenceSection(ReadyToRunSection section)
        {
            int filterOffset = ValidateAndGetSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.AttributePresence,
                nameof(GetAttributePresenceSection));
            int filterSize = section.Size;

            if (filterSize == 0)
            {
                var emptyFilter = new NativeCuckooFilter(_nativeReader, 0, 0);
                return new AttributePresenceSection(emptyFilter);
            }

            if ((filterOffset & 0xF) != 0)
                throw new BadImageFormatException(
                    $"AttributePresence section file offset 0x{filterOffset:X} is not 16-byte aligned.");

            if ((filterSize & (filterSize - 1)) != 0)
                throw new BadImageFormatException(
                    $"AttributePresence section size {filterSize} is not a power of two.");

            if ((filterSize & 0xF) != 0)
                throw new BadImageFormatException(
                    $"AttributePresence section size {filterSize} is not a multiple of 16 bytes.");

            var filter = new NativeCuckooFilter(_nativeReader, filterOffset, filterOffset + filterSize);
            return new AttributePresenceSection(filter);
        }
    }
}
