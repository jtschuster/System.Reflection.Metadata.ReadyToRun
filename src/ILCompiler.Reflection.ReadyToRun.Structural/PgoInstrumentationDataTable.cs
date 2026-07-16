// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;


namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Structural projection of the PgoInstrumentationData section.
    /// A NativeHashtable where each entry points at a method-signature blob immediately
    /// followed by a versionAndFlags word and a (possibly back-referenced) PGO data blob.
    /// Use <see cref="ReadyToRunReader.GetPgoPayload(PgoEntry)"/> to fully decode an entry.
    /// </summary>
    /// <remarks>
    /// Crossgen2 emitter: <c>InstrumentationDataTableNode</c>.
    /// </remarks>
    public sealed class PgoInstrumentationDataTable
    {
        public IReadOnlyList<PgoEntry> Entries { get; }

        internal PgoInstrumentationDataTable(List<PgoEntry> entries)
        {
            Entries = entries;
        }
    }

    public partial class ReadyToRunReader
    {
        public PgoInstrumentationDataTable GetPgoInstrumentationDataTable(ReadyToRunSection section)
        {
            int sectionOffset = ValidateAndGetSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.PgoInstrumentationData,
                nameof(GetPgoInstrumentationDataTable));
            NativeParser parser = new NativeParser(_nativeReader, (uint)sectionOffset);
            NativeHashtable hashtable = new NativeHashtable(_nativeReader, parser, (uint)(sectionOffset + section.Size));
            var enumerator = hashtable.EnumerateAllEntries();
            var entries = new List<PgoEntry>();

            NativeParser curParser = enumerator.GetNext();
            while (!curParser.IsNull())
            {
                int signatureBlobOffset = (int)curParser.Offset;
                byte lowHashcode = curParser.LowHashcode;

                RegisterPayloadRange(
                    signatureBlobOffset,
                    sectionOffset,
                    checked(sectionOffset + section.Size));
                entries.Add(new PgoEntry((PgoPayloadOffset)signatureBlobOffset, lowHashcode));
                curParser = enumerator.GetNext();
            }

            return new PgoInstrumentationDataTable(entries);
        }

        /// <summary>
        /// Fully parse a <see cref="PgoEntry"/>: decode the method signature, then read
        /// the encoded versionAndFlags word and resolve the PGO data blob offset.
        /// </summary>
        /// <remarks>
        /// Payload layout at <see cref="PgoEntry.PayloadOffset"/>:
        /// method-signature || DecodeUnsigned(versionAndFlags) || (optional back-reference) || pgo-data-blob.
        /// The low 2 bits of <c>versionAndFlags</c> are the tag:
        /// <list type="bullet">
        ///   <item><description><c>1</c> — PGO data blob is inline immediately after the versionAndFlags word.</description></item>
        ///   <item><description><c>3</c> — a second <c>DecodeUnsigned</c> follows; subtract that delta from the
        ///     post-versionAndFlags offset to find the deduplicated PGO data blob.</description></item>
        ///   <item><description>any other value — invalid PGO format.</description></item>
        /// </list>
        /// The remaining bits (<c>versionAndFlags &gt;&gt; 2</c>) are the PGO format version.
        /// The PGO data blob itself is a schema-driven sequence of compressed-int records;
        /// decoding it is intentionally left to higher-layer consumers.
        /// </remarks>
        public PgoPayload GetPgoPayload(PgoEntry entry)
            => GetPgoPayload(entry, SignatureDecodingOptions);

        /// <summary>
        /// Fully parse a <see cref="PgoEntry"/> using explicit ReadyToRun version/policy settings.
        /// </summary>
        public PgoPayload GetPgoPayload(PgoEntry entry, IReadyToRunSignatureDecodingOptions options)
        {
            EnsureSemanticDecodingSupported(nameof(GetPgoPayload));
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(options);

            int payloadOffset = (int)entry.PayloadOffset;
            (int sectionOffset, int sectionEndOffset) = GetPayloadRange(payloadOffset, nameof(entry));
            R2RSignatureDecodeResult signature = RawSignatureDecoder.DecodeMethodSignatureWithEndOffset(_nativeReader, payloadOffset, TargetPointerSize, options);
            EnsurePayloadOffsetWithinRange(signature.EndOffset, payloadOffset, allowEndOffset: false);

            var parser = new NativeParser(
                _nativeReader,
                (uint)signature.EndOffset,
                (uint)sectionEndOffset);
            uint versionAndFlags = parser.GetUnsigned();
            int offset = (int)parser.Offset;

            int pgoDataBlobOffset;
            switch (versionAndFlags & 3)
            {
                case 1:
                    // Inline: data follows versionAndFlags directly.
                    pgoDataBlobOffset = offset;
                    break;
                case 3:
                    // Back-reference: subtract delta from post-versionAndFlags offset.
                    uint delta = parser.GetUnsigned();
                    if (delta > (uint)(offset - sectionOffset))
                        throw new BadImageFormatException("PGO data back-reference points outside its containing section.");
                    pgoDataBlobOffset = offset - (int)delta;
                    break;
                default:
                    throw new BadImageFormatException("Invalid PGO instrumentation data format");
            }

            if (pgoDataBlobOffset < sectionOffset || pgoDataBlobOffset >= sectionEndOffset)
                throw new BadImageFormatException("PGO data offset is outside its containing section.");

            int pgoFormatVersion = (int)(versionAndFlags >> 2);
            return new PgoPayload(
                signature.Signature,
                pgoFormatVersion,
                (PgoDataBlobOffset)(uint)pgoDataBlobOffset);
        }
    }

    /// <summary>
    /// Opaque handle to the start of a <see cref="PgoEntry"/> payload
    /// (a method signature blob immediately followed by versionAndFlags and PGO data).
    /// Pass to <see cref="ReadyToRunReader.GetPgoPayload(PgoEntry)"/> to decode.
    /// The underlying value is the file offset (not RVA) of the payload start.
    /// </summary>
    public enum PgoPayloadOffset : uint { }

    /// <summary>Opaque file offset to a PGO schema data blob.</summary>
    public enum PgoDataBlobOffset : uint { }

    /// <summary>
    /// A single entry in the PgoInstrumentationData hashtable.
    /// Holds a handle to the payload and the bucketing hash; use
    /// <see cref="ReadyToRunReader.GetPgoPayload(PgoEntry)"/> to decode the rest.
    /// </summary>
    public sealed class PgoEntry
    {
        /// <summary>Handle to the entry's payload (method signature blob followed by PGO data).</summary>
        public PgoPayloadOffset PayloadOffset { get; }

        /// <summary>Low byte of the hash code used for hashtable bucketing.</summary>
        public byte LowHashcode { get; }

        internal PgoEntry(PgoPayloadOffset payloadOffset, byte lowHashcode)
        {
            PayloadOffset = payloadOffset;
            LowHashcode = lowHashcode;
        }
    }

    /// <summary>
    /// Decoded payload for a <see cref="PgoEntry"/>.
    /// </summary>
    public sealed class PgoPayload
    {
        /// <summary>Raw method signature parts for the method whose PGO data this entry holds.</summary>
        public R2RSignature MethodSignature { get; }

        /// <summary>PGO format version (the high bits of the versionAndFlags word).</summary>
        public int PgoFormatVersion { get; }

        /// <summary>
        /// File offset of the PGO data blob. For back-referenced entries this points at a
        /// previously-emitted (deduplicated) blob and may precede the entry's own offset.
        /// </summary>
        public PgoDataBlobOffset PgoDataBlobOffset { get; }

        internal PgoPayload(
            R2RSignature methodSignature,
            int pgoFormatVersion,
            PgoDataBlobOffset pgoDataBlobOffset)
        {
            MethodSignature = methodSignature;
            PgoFormatVersion = pgoFormatVersion;
            PgoDataBlobOffset = pgoDataBlobOffset;
        }
    }
}
