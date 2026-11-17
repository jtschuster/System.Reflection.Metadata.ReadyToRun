// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace System.Reflection.Metadata.ReadyToRun
{
    public partial class ReadyToRunReader
    {
        /// <summary>
        /// Open a NativeHashtable occupying [<paramref name="tableOffset"/>, <paramref name="tableOffset"/> + <paramref name="tableSize"/>)
        /// in the image. Used by the per-section hashtable handle types, which carry only their image range.
        /// </summary>
        /// <summary>
        /// Validate a section that holds a NativeHashtable and return its image offset. The bucket header is
        /// checked eagerly so a malformed table is rejected when it is opened rather than when it is first used.
        /// </summary>
        private int ValidateAndGetNativeHashtableSectionOffset(
            ReadyToRunSection section,
            Internal.Runtime.ReadyToRunSectionType expectedType,
            string operation)
        {
            int sectionOffset = ValidateAndGetSectionOffset(section, expectedType, operation);
            if (section.Size <= 0)
                throw new BadImageFormatException($"ReadyToRun section {(int)section.Type} is too small to hold a NativeHashtable.");

            _ = new NativeHashtable(_nativeReader, new NativeParser(_nativeReader, (uint)sectionOffset), (uint)(sectionOffset + section.Size));
            return sectionOffset;
        }

        private NativeHashtable OpenNativeHashtable(int tableOffset, int tableSize, string parameterName, out int tableEndOffset)
        {
            if (tableSize <= 0 || tableOffset < 0 || (long)tableOffset + tableSize > ImageEndOffset)
                throw new ArgumentException("The hashtable handle does not belong to this image.", parameterName);

            tableEndOffset = tableOffset + tableSize;
            NativeParser parser = new NativeParser(_nativeReader, (uint)tableOffset);
            return new NativeHashtable(_nativeReader, parser, (uint)tableEndOffset);
        }

        /// <summary>
        /// Lazily enumerate every entry of a NativeHashtable, yielding entry offsets relative to the table start.
        /// The table range is validated eagerly.
        /// </summary>
        private IEnumerable<uint> EnumerateNativeHashtableEntries(int tableOffset, int tableSize, string parameterName)
        {
            NativeHashtable hashtable = OpenNativeHashtable(tableOffset, tableSize, parameterName, out int tableEndOffset);
            return Enumerate(hashtable, tableOffset, tableEndOffset);

            static IEnumerable<uint> Enumerate(NativeHashtable hashtable, int tableOffset, int tableEndOffset)
            {
                NativeHashtable.AllEntriesEnumerator enumerator = hashtable.EnumerateAllEntries();
                for (NativeParser parser = enumerator.GetNext(); !parser.IsNull(); parser = enumerator.GetNext())
                    yield return ToTableRelativeOffset(parser.Offset, tableOffset, tableEndOffset);
            }
        }

        /// <summary>
        /// Lazily enumerate the entries in the bucket for <paramref name="hashcode"/> whose low hashcode byte matches,
        /// yielding entry offsets relative to the table start. The table range is validated eagerly.
        /// </summary>
        private IEnumerable<uint> LookupNativeHashtableEntries(int tableOffset, int tableSize, int hashcode, string parameterName)
        {
            NativeHashtable hashtable = OpenNativeHashtable(tableOffset, tableSize, parameterName, out int tableEndOffset);
            return Lookup(hashtable, hashcode, tableOffset, tableEndOffset);

            static IEnumerable<uint> Lookup(NativeHashtable hashtable, int hashcode, int tableOffset, int tableEndOffset)
            {
                NativeHashtable.Enumerator enumerator = hashtable.Lookup(hashcode);
                for (NativeParser parser = enumerator.GetNext(); !parser.IsNull(); parser = enumerator.GetNext())
                    yield return ToTableRelativeOffset(parser.Offset, tableOffset, tableEndOffset);
            }
        }

        /// <summary>
        /// Create a parser positioned at a table-relative hashtable entry and bounded by the end of the table.
        /// </summary>
        private NativeParser OpenNativeHashtableEntry(int tableOffset, int tableSize, uint entryHandle, string parameterName, out int tableEndOffset)
        {
            if (tableSize <= 0 || tableOffset < 0 || (long)tableOffset + tableSize > ImageEndOffset)
                throw new ArgumentException("The hashtable handle does not belong to this image.", parameterName);

            tableEndOffset = tableOffset + tableSize;
            int entryOffset = ToTableEntryImageOffset(entryHandle, tableOffset, tableEndOffset);
            return new NativeParser(_nativeReader, (uint)entryOffset, (uint)tableEndOffset);
        }

        /// <summary>
        /// Convert an image offset produced by a hashtable enumerator into an offset relative to the table start,
        /// validating that it lies inside the table.
        /// </summary>
        private static uint ToTableRelativeOffset(uint imageOffset, int tableOffset, int tableEndOffset)
        {
            EnsureOffsetWithinRange((int)imageOffset, tableOffset, tableEndOffset);
            return imageOffset - (uint)tableOffset;
        }

        /// <summary>
        /// Resolve a table-relative entry offset to an image offset, validating that it lies inside the table.
        /// </summary>
        private static int ToTableEntryImageOffset(uint tableRelativeOffset, int tableOffset, int tableEndOffset)
        {
            long imageOffset = (long)tableOffset + tableRelativeOffset;
            if (imageOffset >= tableEndOffset)
                throw new BadImageFormatException("Hashtable entry handle is outside its table.");
            return (int)imageOffset;
        }
    }
}
