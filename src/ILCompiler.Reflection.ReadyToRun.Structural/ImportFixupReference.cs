// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Structural representation of a type or module reference encoded as a pair of
    /// import-section index and fixup index within that section.
    /// </summary>
    /// <remarks>
    /// In the image, references are encoded as two consecutive NativeFormat unsigned integers:
    /// (importSectionIndex, fixupIndex). They do NOT resolve to TypeDesc or Module;
    /// they are structural indices only.
    /// Crossgen2 emitter: <c>ImportReferenceProvider.EncodeReferenceToType</c>.
    /// </remarks>
    public readonly struct ImportFixupReference : IEquatable<ImportFixupReference>
    {
        /// <summary>Index into the import sections table (corresponds to an ImportSections entry).</summary>
        public uint ImportSectionIndex { get; }

        /// <summary>Fixup index within the import section (0-based offset into the section's fixup cell array).</summary>
        public uint FixupIndex { get; }

        public ImportFixupReference(uint importSectionIndex, uint fixupIndex)
        {
            ImportSectionIndex = importSectionIndex;
            FixupIndex = fixupIndex;
        }

        public bool Equals(ImportFixupReference other)
            => ImportSectionIndex == other.ImportSectionIndex && FixupIndex == other.FixupIndex;

        public override bool Equals(object obj)
            => obj is ImportFixupReference other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(ImportSectionIndex, FixupIndex);

        public override string ToString()
            => $"ImportSection[{ImportSectionIndex}][{FixupIndex}]";
    }
}
