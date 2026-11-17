// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;

namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Handle to the NativeHashtable stored in an AvailableTypes section.
    /// Obtain one from <see cref="ReadyToRunReader.GetAvailableTypesTable(ReadyToRunSection)"/>.
    /// </summary>
    /// <remarks>
    /// <code>
    /// NativeHashtable (hashed by the version-resilient hash of the type's namespace and name)
    ///   entry: DecodeUnsigned((rid &lt;&lt; 1) | isExportedType)
    /// </code>
    /// Crossgen2 emitter: <c>TypesTableNode</c>.
    /// </remarks>
    public readonly struct AvailableTypesHashTableHandle
    {
        internal int Offset { get; }

        internal int Size { get; }

        internal AvailableTypesHashTableHandle(int offset, int size)
        {
            Offset = offset;
            Size = size;
        }
    }

    public partial class ReadyToRunReader
    {
        /// <summary>
        /// Open the AvailableTypes section as a NativeHashtable handle without decoding any entries.
        /// </summary>
        public AvailableTypesHashTableHandle GetAvailableTypesTable(ReadyToRunSection section)
        {
            int sectionOffset = ValidateAndGetNativeHashtableSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.AvailableTypes,
                nameof(GetAvailableTypesTable));
            return new AvailableTypesHashTableHandle(sectionOffset, section.Size);
        }

        /// <summary>Lazily enumerate every entry in the AvailableTypes hashtable.</summary>
        public IEnumerable<AvailableTypeEntryHandle> EnumerateAvailableTypeEntries(AvailableTypesHashTableHandle table)
        {
            foreach (uint entry in EnumerateNativeHashtableEntries(table.Offset, table.Size, nameof(table)))
                yield return (AvailableTypeEntryHandle)entry;
        }

        /// <summary>
        /// Probe the bucket for <paramref name="versionResilientHash"/> and return the first entry that satisfies
        /// <paramref name="predicate"/>.
        /// </summary>
        public AvailableTypeEntryHandle? LookupAvailableTypeEntry(
            AvailableTypesHashTableHandle table,
            int versionResilientHash,
            Func<AvailableTypeEntry, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            foreach (uint entry in LookupNativeHashtableEntries(table.Offset, table.Size, versionResilientHash, nameof(table)))
            {
                AvailableTypeEntryHandle handle = (AvailableTypeEntryHandle)entry;
                if (predicate(GetAvailableTypeEntry(table, handle)))
                    return handle;
            }

            return null;
        }

        /// <summary>Decode one AvailableTypes entry.</summary>
        public AvailableTypeEntry GetAvailableTypeEntry(AvailableTypesHashTableHandle table, AvailableTypeEntryHandle handle)
        {
            NativeParser parser = OpenNativeHashtableEntry(table.Offset, table.Size, (uint)handle, nameof(table), out _);
            uint ridAndFlag = parser.GetUnsigned();
            bool isExportedType = (ridAndFlag & 1) != 0;
            int rid = (int)(ridAndFlag >> 1);
            if (rid > 0xFFFFFF)
                throw new BadImageFormatException("AvailableTypes RID exceeds the metadata row-number range.");

            EntityHandle type = isExportedType
                ? MetadataTokens.ExportedTypeHandle(rid)
                : MetadataTokens.TypeDefinitionHandle(rid);
            return new AvailableTypeEntry(type);
        }
    }

    /// <summary>
    /// A single decoded entry in the AvailableTypes table.
    /// </summary>
    public sealed class AvailableTypeEntry
    {

        /// <summary>
        /// The available type: a <see cref="TypeDefinitionHandle"/> when <see cref="IsExportedType"/> is false,
        /// otherwise an <see cref="ExportedTypeHandle"/>.
        /// </summary>
        public EntityHandle Type { get; }

        /// <summary>Whether this entry refers to an ExportedType (true) or TypeDef (false).</summary>
        public bool IsExportedType => Type.Kind == HandleKind.ExportedType;

        internal AvailableTypeEntry(EntityHandle type)
        {
            Type = type;
        }
    }
}
