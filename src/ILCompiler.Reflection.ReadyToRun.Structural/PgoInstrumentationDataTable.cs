// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Handle to the NativeHashtable stored in a PgoInstrumentationData section.
    /// Obtain one from <see cref="ReadyToRunReader.GetPgoInstrumentationDataTable(ReadyToRunSection)"/>.
    /// </summary>
    /// <remarks>
    /// <code>
    /// NativeHashtable entry:
    ///   method signature | DecodeUnsigned(versionAndFlags)
    ///   [tag 3: DecodeUnsigned(back-reference delta)]
    ///   [PGO data blob (schema-driven, no length prefix)]
    /// </code>
    /// Addressing composes by addition:
    /// <c>table start + <see cref="PgoEntryHandle"/> + entry-relative blob displacement = <see cref="PgoDataBlobImageOffset"/></c>.
    /// Crossgen2 emitter: <c>InstrumentationDataTableNode</c>.
    /// </remarks>
    public readonly struct PgoHashTableHandle
    {
        internal int Offset { get; }

        internal int Size { get; }

        internal PgoHashTableHandle(int offset, int size)
        {
            Offset = offset;
            Size = size;
        }
    }

    public partial class ReadyToRunReader
    {
        /// <summary>
        /// Open the PgoInstrumentationData section as a NativeHashtable handle without decoding any entries.
        /// </summary>
        public PgoHashTableHandle GetPgoInstrumentationDataTable(ReadyToRunSection section)
        {
            int sectionOffset = ValidateAndGetNativeHashtableSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.PgoInstrumentationData,
                nameof(GetPgoInstrumentationDataTable));
            return new PgoHashTableHandle(sectionOffset, section.Size);
        }

        private NativeHashtable OpenPgoHashtable(PgoHashTableHandle table, out int tableOffset, out int tableEndOffset)
        {
            tableOffset = table.Offset;
            return OpenNativeHashtable(table.Offset, table.Size, nameof(table), out tableEndOffset);
        }

        /// <summary>
        /// Lazily enumerate every entry in the PGO hashtable. Each handle locates the entry's
        /// method signature, relative to the start of the table.
        /// </summary>
        public IEnumerable<PgoEntryHandle> EnumeratePgoEntries(PgoHashTableHandle table)
        {
            NativeHashtable hashtable = OpenPgoHashtable(table, out int tableOffset, out int tableEndOffset);
            NativeHashtable.AllEntriesEnumerator enumerator = hashtable.EnumerateAllEntries();

            for (NativeParser curParser = enumerator.GetNext(); !curParser.IsNull(); curParser = enumerator.GetNext())
            {
                int entryOffset = (int)curParser.Offset;
                EnsureOffsetWithinRange(entryOffset, tableOffset, tableEndOffset);
                yield return (PgoEntryHandle)(entryOffset - tableOffset);
            }
        }

        /// <summary>
        /// Probe the PGO hashtable bucket for <paramref name="versionResilientHash"/> and return the
        /// first entry whose method signature satisfies <paramref name="predicate"/>.
        /// </summary>
        /// <returns>The matching entry, or <see langword="null"/> if no entry matches.</returns>
        public PgoEntryHandle? LookupPgoEntry(PgoHashTableHandle table, int versionResilientHash, Func<MethodSignature, bool> predicate)
            => LookupPgoEntry(table, versionResilientHash, predicate, SignatureDecodingOptions);

        /// <summary>
        /// Look up a PGO entry using explicit ReadyToRun version/policy settings.
        /// </summary>
        public PgoEntryHandle? LookupPgoEntry(
            PgoHashTableHandle table,
            int versionResilientHash,
            Func<MethodSignature, bool> predicate,
            IReadyToRunSignatureDecodingOptions options)
        {
            EnsureSemanticDecodingSupported(nameof(LookupPgoEntry));
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(options);

            NativeHashtable hashtable = OpenPgoHashtable(table, out int tableOffset, out int tableEndOffset);
            NativeHashtable.Enumerator enumerator = hashtable.Lookup(versionResilientHash);

            for (NativeParser entryParser = enumerator.GetNext(); !entryParser.IsNull(); entryParser = enumerator.GetNext())
            {
                int entryOffset = (int)entryParser.Offset;
                EnsureOffsetWithinRange(entryOffset, tableOffset, tableEndOffset);
                R2RSignatureDecodeResult signature = RawSignatureDecoder.DecodeMethodSignatureWithEndOffset(_nativeReader, entryOffset, TargetPointerSize, options);
                EnsureOffsetWithinRange(signature.EndOffset, tableOffset, tableEndOffset);
                if (predicate(MethodSignature.FromSignature(signature.Signature)))
                    return (PgoEntryHandle)(entryOffset - tableOffset);
            }

            return null;
        }

        /// <summary>
        /// Decode a PGO entry: the method signature, the encoded versionAndFlags word, and the
        /// location of its PGO data blob.
        /// </summary>
        /// <remarks>
        /// Entry layout: method-signature || DecodeUnsigned(versionAndFlags) || (optional back-reference) || pgo-data-blob.
        /// The low 2 bits of <c>versionAndFlags</c> are the tag:
        /// <list type="bullet">
        ///   <item><description><c>1</c> — PGO data blob is inline immediately after the versionAndFlags word.</description></item>
        ///   <item><description><c>3</c> — a second <c>DecodeUnsigned</c> follows; subtract that delta from the
        ///     offset immediately after <c>versionAndFlags</c> (the start of the encoded delta) to find the
        ///     deduplicated PGO data blob.</description></item>
        ///   <item><description>any other value — invalid PGO format.</description></item>
        /// </list>
        /// The remaining bits (<c>versionAndFlags &gt;&gt; 2</c>) are the PGO format version.
        /// The PGO data blob is a schema-driven sequence of compressed-int records with no length prefix;
        /// decoding it is left to higher-layer consumers.
        /// </remarks>
        public PgoEntry GetPgoEntry(PgoHashTableHandle table, PgoEntryHandle handle)
            => GetPgoEntry(table, handle, SignatureDecodingOptions);

        /// <summary>
        /// Decode a PGO entry using explicit ReadyToRun version/policy settings.
        /// </summary>
        public PgoEntry GetPgoEntry(PgoHashTableHandle table, PgoEntryHandle handle, IReadyToRunSignatureDecodingOptions options)
        {
            EnsureSemanticDecodingSupported(nameof(GetPgoEntry));
            ArgumentNullException.ThrowIfNull(options);

            _ = OpenPgoHashtable(table, out int tableOffset, out int tableEndOffset);
            int entryOffset = checked(tableOffset + (int)(uint)handle);
            EnsureOffsetWithinRange(entryOffset, tableOffset, tableEndOffset);
            R2RSignatureDecodeResult signature = RawSignatureDecoder.DecodeMethodSignatureWithEndOffset(_nativeReader, entryOffset, TargetPointerSize, options);
            EnsureOffsetWithinRange(signature.EndOffset, tableOffset, tableEndOffset);

            var parser = new NativeParser(_nativeReader, (uint)signature.EndOffset, (uint)tableEndOffset);
            uint versionAndFlags = parser.GetUnsigned();
            PgoDataBlobBackReferenceDelta? backReferenceDelta = null;
            int blobOffset;
            switch (versionAndFlags & 3)
            {
                case 1:
                    blobOffset = (int)parser.Offset;
                    break;
                case 3:
                    int backReferenceOrigin = (int)parser.Offset;
                    uint delta = parser.GetUnsigned();
                    if (delta > (uint)(backReferenceOrigin - tableOffset))
                        throw new BadImageFormatException("PGO data back-reference points outside the PgoInstrumentationData section.");
                    backReferenceDelta = (PgoDataBlobBackReferenceDelta)delta;
                    blobOffset = backReferenceOrigin - (int)delta;
                    break;
                default:
                    throw new BadImageFormatException("Invalid PGO instrumentation data format");
            }

            EnsureOffsetWithinRange(blobOffset, tableOffset, tableEndOffset);
            return new PgoEntry(
                signature.Signature,
                (int)(versionAndFlags >> 2),
                backReferenceDelta,
                blobOffset - tableOffset);
        }

        /// <summary>
        /// Resolve the image offset of an entry's PGO data blob, whether inline or back-referenced.
        /// </summary>
        /// <param name="table">The hashtable that <paramref name="entry"/> was read from.</param>
        /// <param name="entry">An entry from <see cref="GetPgoEntry(PgoHashTableHandle, PgoEntryHandle)"/>.</param>
        public PgoDataBlobImageOffset GetPgoDataBlobImageOffset(PgoHashTableHandle table, PgoEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            _ = OpenPgoHashtable(table, out int tableOffset, out int tableEndOffset);
            long blobOffset = (long)tableOffset + entry.BlobOffsetFromTable;
            if (blobOffset < tableOffset || blobOffset >= tableEndOffset)
                throw new BadImageFormatException("PGO data blob lies outside the PgoInstrumentationData section.");
            return (PgoDataBlobImageOffset)(int)blobOffset;
        }
    }

    /// <summary>
    /// Decoded PGO hashtable entry. Use <see cref="ReadyToRunReader.GetPgoDataBlobImageOffset"/>
    /// to locate its PGO data blob.
    /// </summary>
    /// <remarks>
    /// <code>
    /// table start + entry handle -> signature and flags -> inline or back-referenced blob
    /// </code>
    /// </remarks>
    public sealed class PgoEntry
    {

        /// <summary>Raw method signature parts for the method whose PGO data this entry holds.</summary>
        public R2RSignature MethodSignature { get; }

        /// <summary>PGO format version (the high bits of the versionAndFlags word).</summary>
        public int PgoFormatVersion { get; }

        /// <summary>
        /// Raw encoded backward-reference delta (tag 3), or <see langword="null"/> when the blob
        /// immediately follows the <c>versionAndFlags</c> value (tag 1). Provided for format inspection;
        /// use <see cref="ReadyToRunReader.GetPgoDataBlobImageOffset"/> to locate the blob.
        /// </summary>
        public PgoDataBlobBackReferenceDelta? PgoDataBlobBackReferenceDelta { get; }

        // Blob start relative to the start of the hashtable.
        internal int BlobOffsetFromTable { get; }

        internal PgoEntry(
            R2RSignature methodSignature,
            int pgoFormatVersion,
            PgoDataBlobBackReferenceDelta? pgoDataBlobBackReferenceDelta,
            int blobOffsetFromTable)
        {
            MethodSignature = methodSignature;
            PgoFormatVersion = pgoFormatVersion;
            PgoDataBlobBackReferenceDelta = pgoDataBlobBackReferenceDelta;
            BlobOffsetFromTable = blobOffsetFromTable;
        }
    }
}
