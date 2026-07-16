// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Internal.Runtime;

namespace System.Reflection.Metadata.ReadyToRun
{
    /// <remarks>
    /// Crossgen2 emitter: <c>ReadyToRunHeaderNode (each entry corresponds to one section registered via Header.Add)</c>.
    /// </remarks>
    public readonly struct ReadyToRunSection
    {
        /// <summary>
        /// The ReadyToRun section type
        /// </summary>
        public ReadyToRunSectionType Type { get; }

        /// <summary>
        /// The raw RVA to the section. This is not a PCode value, including for
        /// <see cref="ReadyToRunSectionType.DelayLoadMethodCallThunks"/>.
        /// </summary>
        public ImageRVA RelativeVirtualAddress { get; }

        /// <summary>
        /// The size of the section
        /// </summary>
        public int Size { get; }

        internal ReadyToRunSection(ReadyToRunSectionType type, ImageRVA rva, int size)
        {
            Type = type;
            RelativeVirtualAddress = rva;
            Size = size;
        }

        public override string ToString()
            => $"{Type}: RVA 0x{(uint)RelativeVirtualAddress:X8}, Size {Size}";
    }

    /// <summary>Opaque handle representing an RVA pointing to the start of a ReadyToRun section.</summary>
    public enum ImageRVA : uint {}

    /// <summary>
    /// Opaque handle representing the raw RVA of the DelayLoadMethodCallThunks section.
    /// This names the value separately from <see cref="PCode"/> because the section header
    /// entry is not a target code pointer.
    /// </summary>
    public enum DelayLoadMethodThunkRva : uint {}
}
