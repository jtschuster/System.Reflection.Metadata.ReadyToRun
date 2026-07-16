// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Structural projection of the EnclosingTypeMap section.
    /// Maps each TypeDef (1-based RID) to the RID of its enclosing type (0 if not nested).
    /// </summary>
    /// <remarks>
    /// Crossgen2 emitter: <c>EnclosingTypeMapNode</c>.
    /// </remarks>
    public sealed class EnclosingTypeMapTable
    {
        /// <summary>Number of TypeDef entries in the map.</summary>
        public int Count { get; }

        internal ushort[] EnclosingTypeRids { get; }

        internal EnclosingTypeMapTable(int count, ushort[] enclosingTypeRids)
        {
            Count = count;
            EnclosingTypeRids = enclosingTypeRids;
        }
    }

    public partial class ReadyToRunReader
    {
        public EnclosingTypeMapTable GetEnclosingTypeMapTable(ReadyToRunSection section)
        {
            int offset = ValidateAndGetSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.EnclosingTypeMap,
                nameof(GetEnclosingTypeMapTable));
            if (section.Size < sizeof(ushort))
                throw new BadImageFormatException("EnclosingTypeMap section is missing its entry count.");

            ushort count = _nativeReader.ReadUInt16(ref offset);
            int expectedSize = checked(sizeof(ushort) + (count * sizeof(ushort)));
            if (section.Size != expectedSize)
            {
                throw new BadImageFormatException(
                    $"EnclosingTypeMap section size {section.Size} does not match its encoded count {count}.");
            }

            ushort[] rids = new ushort[count];

            for (int i = 0; i < count; i++)
                rids[i] = _nativeReader.ReadUInt16(ref offset);

            return new EnclosingTypeMapTable(count, rids);
        }

        /// <summary>
        /// Returns the enclosing type RID for the TypeDef at the given 1-based RID.
        /// Returns 0 if the type is not nested or the RID is out of range.
        /// </summary>
        public int GetEnclosingTypeRid(EnclosingTypeMapTable table, int typeDefRid)
        {
            EnsureSemanticDecodingSupported(nameof(GetEnclosingTypeRid));
            if (typeDefRid < 1 || typeDefRid > table.Count)
                return 0;

            return table.EnclosingTypeRids[typeDefRid - 1];
        }
    }
}
