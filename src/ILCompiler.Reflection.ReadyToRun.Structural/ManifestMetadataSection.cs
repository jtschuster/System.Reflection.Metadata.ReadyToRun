// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Structural projection of the ManifestMetadata section (section 112).
    /// Exposes the raw blob descriptor — RVA and byte size — of the manifest metadata block.
    /// </summary>
    /// <remarks>
    /// The manifest metadata is a ECMA-335 metadata blob embedded in the R2R image.
    /// Use <see cref="ReadyToRunReader.GetManifestMetadataReader"/> for semantic access via
    /// <see cref="MetadataReader"/>; this class exposes the structural location only.
    /// </remarks>
    public sealed class ManifestMetadataSection
    {
        /// <summary>The RVA of the manifest metadata blob.</summary>
        public ImageRVA SectionRva { get; }

        /// <summary>The byte size of the manifest metadata blob.</summary>
        public int SectionSize { get; }

        internal ManifestMetadataSection(ImageRVA sectionRva, int sectionSize)
        {
            SectionRva = sectionRva;
            SectionSize = sectionSize;
        }
    }

    public partial class ReadyToRunReader
    {
        /// <summary>
        /// Returns a structural blob descriptor for the ManifestMetadata section.
        /// </summary>
        /// <remarks>
        /// This returns the raw RVA and size of the manifest metadata blob.
        /// For semantic access, use <see cref="GetManifestMetadataReader"/> instead.
        /// </remarks>
        public ManifestMetadataSection GetManifestMetadataSection(ReadyToRunSection section)
        {
            ValidateAndGetSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.ManifestMetadata,
                nameof(GetManifestMetadataSection));
            return new ManifestMetadataSection(section.RelativeVirtualAddress, section.Size);
        }
    }
}
