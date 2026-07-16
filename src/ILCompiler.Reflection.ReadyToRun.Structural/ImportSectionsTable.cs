// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Reflection.PortableExecutable;

using Internal.ReadyToRunConstants;


namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Structural projection of the ImportsTable section.
    /// Each entry is a raw import section descriptor without decoded signatures.
    /// </summary>
    /// <remarks>
    /// Crossgen2 emitter: <c>ImportSectionsTableNode</c> (entries are <c>ImportSectionNode</c> instances).
    /// </remarks>
    public sealed class ImportSectionsTableSection
    {
        public IReadOnlyList<ImportSectionEntry> Entries { get; }

        internal ImportSectionsTableSection(List<ImportSectionEntry> entries)
        {
            Entries = entries;
        }
    }

    /// <summary>
    /// A single raw import section descriptor.
    /// </summary>
    public sealed class ImportSectionEntry
    {
        /// <summary>RVA of the section containing values to be fixed up.</summary>
        public ImportSlotTableRva SectionRva { get; }

        /// <summary>Size of the section in bytes.</summary>
        public int SectionSize { get; }

        /// <summary>Import section flags.</summary>
        public ReadyToRunImportSectionFlags Flags { get; }

        /// <summary>Import section type.</summary>
        public ReadyToRunImportSectionType Type { get; }

        /// <summary>Encoded entry size byte. Zero means use the machine pointer size.</summary>
        public byte EncodedEntrySize { get; }

        /// <summary>RVA of the signature indirection table for this section.</summary>
        public SignatureTableRva SignatureTableRva { get; }

        /// <summary>RVA of optional auxiliary data (typically GC info).</summary>
        public AuxiliaryDataTableRva AuxiliaryDataRva { get; }

        internal ImportSectionEntry(
            ImportSlotTableRva sectionRva,
            int sectionSize,
            ReadyToRunImportSectionFlags flags,
            ReadyToRunImportSectionType type,
            byte encodedEntrySize,
            SignatureTableRva signatureTableRva,
            AuxiliaryDataTableRva auxiliaryDataRva)
        {
            SectionRva = sectionRva;
            SectionSize = sectionSize;
            Flags = flags;
            Type = type;
            EncodedEntrySize = encodedEntrySize;
            SignatureTableRva = signatureTableRva;
            AuxiliaryDataRva = auxiliaryDataRva;
        }
    }

    public partial class ReadyToRunReader
    {
        public ImportSectionsTableSection GetImportSectionsTableSection(ReadyToRunSection section)
        {
            int offset = ValidateAndGetSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.ImportSections,
                nameof(GetImportSectionsTableSection));
            const int descriptorSize = 5 * sizeof(int);
            if (section.Size % descriptorSize != 0)
            {
                throw new BadImageFormatException(
                    $"ImportSections section size {section.Size} is not divisible by descriptor size {descriptorSize}.");
            }

            int count = section.Size / descriptorSize;
            var entries = new List<ImportSectionEntry>();

            for (int i = 0; i < count; i++)
            {
                int sectionRva = this.ImageReader.ReadInt32(ref offset);
                int sectionSize = this.ImageReader.ReadInt32(ref offset);
                var flags = (ReadyToRunImportSectionFlags)this.ImageReader.ReadUInt16(ref offset);
                var type = (ReadyToRunImportSectionType)this.ImageReader.ReadByte(ref offset);
                byte encodedEntrySize = this.ImageReader.ReadByte(ref offset);

                int signatureRva = this.ImageReader.ReadInt32(ref offset);
                int auxiliaryDataRva = this.ImageReader.ReadInt32(ref offset);
                if (sectionSize < 0)
                    throw new BadImageFormatException("Import section descriptor contains a negative section size.");

                if (ValidationMode == ReadyToRunValidationMode.Strict)
                {
                    const ReadyToRunImportSectionFlags knownFlags =
                        ReadyToRunImportSectionFlags.Eager | ReadyToRunImportSectionFlags.PCode;
                    ReadyToRunImportSectionFlags unknownFlags = flags & ~knownFlags;
                    if (unknownFlags != 0)
                    {
                        throw new NotSupportedException(
                            $"Import section flags 0x{(ushort)unknownFlags:X4} are not supported.");
                    }

                    if (!System.Enum.IsDefined(typeof(ReadyToRunImportSectionType), type))
                        throw new NotSupportedException($"Import section type {(byte)type} is not supported.");
                }

                entries.Add(new ImportSectionEntry(
                    (ImportSlotTableRva)sectionRva,
                    sectionSize,
                    flags,
                    type,
                    encodedEntrySize,
                    (SignatureTableRva)signatureRva,
                    (AuxiliaryDataTableRva)auxiliaryDataRva));
            }

            return new ImportSectionsTableSection(entries);
        }

        public int GetImportSectionEntrySize(ImportSectionEntry entry)
        {
            EnsureSemanticDecodingSupported(nameof(GetImportSectionEntrySize));
            if (entry.EncodedEntrySize != 0)
                return entry.EncodedEntrySize;

            return TargetPointerSize;
        }

        public int GetImportSectionEntryCount(ImportSectionEntry entry)
        {
            EnsureSemanticDecodingSupported(nameof(GetImportSectionEntryCount));
            int entrySize = GetImportSectionEntrySize(entry);
            if (entry.SectionSize < 0 || entry.SectionSize % entrySize != 0)
            {
                throw new BadImageFormatException(
                    $"Import section size {entry.SectionSize} is not divisible by entry size {entrySize}.");
            }

            return entry.SectionSize / entrySize;
        }
    }

    public enum SignatureRva : uint {}
    public enum SignatureTableRva : uint {}
    public enum ImportSlotTableRva : uint {}
    public enum AuxiliaryDataTableRva : uint {}
}
