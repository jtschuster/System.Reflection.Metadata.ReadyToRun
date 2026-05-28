// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

using Internal.Runtime;

namespace System.Reflection.Metadata.ReadyToRun
{
    /// <remarks>
    /// Crossgen2 emitter: <c>ReadyToRunHeaderNode (each entry corresponds to one section registered via Header.Add)</c>.
    /// </remarks>
    public struct ReadyToRunSection
    {
        /// <summary>
        /// The ReadyToRun section type
        /// </summary>
        public ReadyToRunSectionType Type { get; set; }

        /// <summary>
        /// The raw RVA to the section. This is not a PCode value, including for
        /// <see cref="ReadyToRunSectionType.DelayLoadMethodCallThunks"/>.
        /// </summary>
        public ImageRVA RelativeVirtualAddress { get; set; }

        /// <summary>
        /// The size of the section
        /// </summary>
        public int Size { get; set; }

        public ReadyToRunSection(ReadyToRunSectionType type, ImageRVA rva, int size)
        {
            Type = type;
            RelativeVirtualAddress = rva;
            Size = size;
        }

        /// <summary>
        /// Gets the raw RVA for a <see cref="ReadyToRunSectionType.DelayLoadMethodCallThunks"/> section.
        /// The section contains executable thunks, but the header section entry itself is a raw RVA,
        /// not a PCode value.
        /// </summary>
        public DelayLoadMethodThunkRva DelayLoadMethodThunkRva
        {
            get
            {
                if (Type != ReadyToRunSectionType.DelayLoadMethodCallThunks)
                {
                    throw new InvalidOperationException(
                        $"{nameof(DelayLoadMethodThunkRva)} is only valid for {ReadyToRunSectionType.DelayLoadMethodCallThunks} sections.");
                }

                return (DelayLoadMethodThunkRva)(int)RelativeVirtualAddress;
            }
        }

        public override string ToString()
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>Opaque handle representing an RVA pointing to the start of a ReadyToRun section.</summary>
    public enum ImageRVA {}

    /// <summary>
    /// Opaque handle representing the raw RVA of the DelayLoadMethodCallThunks section.
    /// This names the value separately from <see cref="PCode"/> because the section header
    /// entry is not a target code pointer.
    /// </summary>
    public enum DelayLoadMethodThunkRva : uint {}
}
