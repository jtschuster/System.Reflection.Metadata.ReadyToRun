// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;


namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Structural projection of the ManifestAssemblyMvids section (section 118).
    /// Contains a sequential list of 16-byte GUIDs, one per manifest assembly.
    /// </summary>
    /// <remarks>
    /// Crossgen2 emitter: <c>ManifestAssemblyMvidHeaderNode</c>.
    /// The section is a flat array of GUIDs with no header or count prefix;
    /// the count is derived from section size divided by 16.
    /// </remarks>
    public sealed class ManifestAssemblyMvidsTable
    {
        /// <summary>The GUIDs of the manifest assemblies, in declaration order.</summary>
        public IReadOnlyList<Guid> Mvids { get; }

        internal ManifestAssemblyMvidsTable(IReadOnlyList<Guid> mvids)
        {
            Mvids = mvids;
        }
    }

    public partial class ReadyToRunReader
    {
        /// <summary>
        /// Parses the ManifestAssemblyMvids section as a sequential list of 16-byte GUIDs.
        /// </summary>
        /// <exception cref="BadImageFormatException">
        /// Thrown when the section size is not a multiple of 16 (truncated).
        /// </exception>
        public ManifestAssemblyMvidsTable GetManifestAssemblyMvidsTable(ReadyToRunSection section)
        {
            if (section.Size % 16 != 0)
                throw new BadImageFormatException(
                    $"ManifestAssemblyMvids section size {section.Size} is not a multiple of 16 bytes.");

            int count = section.Size / 16;
            if (count == 0)
                return new ManifestAssemblyMvidsTable([]);

            var mvids = new List<Guid>(count);
            int offset = GetOffsetForRVA(section.RelativeVirtualAddress);

            for (int i = 0; i < count; i++)
            {
                // Read 16 bytes and interpret as a little-endian GUID.
                byte[] guidBytes = new byte[16];
                _nativeReader.ReadSpanAt(ref offset, guidBytes);
                mvids.Add(new Guid(guidBytes));
            }

            return new ManifestAssemblyMvidsTable(mvids);
        }
    }
}
