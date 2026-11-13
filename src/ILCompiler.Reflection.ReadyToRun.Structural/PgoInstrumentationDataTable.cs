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
            int sectionEndOffset = checked(sectionOffset + section.Size);
            NativeParser parser = new NativeParser(_nativeReader, (uint)sectionOffset);
            NativeHashtable hashtable = new NativeHashtable(_nativeReader, parser, (uint)sectionEndOffset);
            var enumerator = hashtable.EnumerateAllEntries();
            var entries = new List<PgoEntry>();

            NativeParser curParser = enumerator.GetNext();
            while (!curParser.IsNull())
            {
                int signatureBlobOffset = (int)curParser.Offset;
                byte lowHashcode = curParser.LowHashcode;

                EnsureOffsetWithinRange(signatureBlobOffset, sectionOffset, sectionEndOffset);
                entries.Add(new PgoEntry((PgoPayloadOffset)signatureBlobOffset, lowHashcode));
                curParser = enumerator.GetNext();
            }

            return new PgoInstrumentationDataTable(entries);
        }

        /// <summary>
        /// Parse a <see cref="PgoEntry"/>: decode the method signature, then read
        /// the encoded versionAndFlags word and optional PGO data back-reference delta.
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
        /// Parse a <see cref="PgoEntry"/> using explicit ReadyToRun version/policy settings.
        /// </summary>
        public PgoPayload GetPgoPayload(PgoEntry entry, IReadyToRunSignatureDecodingOptions options)
        {
            EnsureSemanticDecodingSupported(nameof(GetPgoPayload));
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(options);

            int payloadOffset = (int)entry.PayloadOffset;
            int imageEndOffset = ImageEndOffset;
            EnsureOffsetWithinRange(payloadOffset, 0, imageEndOffset);
            R2RSignatureDecodeResult signature = RawSignatureDecoder.DecodeMethodSignatureWithEndOffset(_nativeReader, payloadOffset, TargetPointerSize, options);
            EnsureOffsetWithinRange(signature.EndOffset, 0, imageEndOffset);

            var parser = new NativeParser(
                _nativeReader,
                (uint)signature.EndOffset,
                (uint)imageEndOffset);
            uint versionAndFlags = parser.GetUnsigned();
            PgoDataBlobBackReferenceDelta? pgoDataBlobBackReferenceDelta = null;
            switch (versionAndFlags & 3)
            {
                case 1:
                    break;
                case 3:
                    // Preserve the encoded delta; do not turn it into an image offset.
                    uint delta = parser.GetUnsigned();
                    if (delta > parser.Offset)
                        throw new BadImageFormatException("PGO data back-reference points outside the image.");
                    pgoDataBlobBackReferenceDelta = (PgoDataBlobBackReferenceDelta)delta;
                    break;
                default:
                    throw new BadImageFormatException("Invalid PGO instrumentation data format");
            }

            int pgoFormatVersion = (int)(versionAndFlags >> 2);
            return new PgoPayload(
                signature.Signature,
                pgoFormatVersion,
                pgoDataBlobBackReferenceDelta);
        }
    }

    /// <summary>
    /// Opaque handle to the start of a <see cref="PgoEntry"/> payload
    /// (a method signature blob immediately followed by versionAndFlags and PGO data).
    /// Pass to <see cref="ReadyToRunReader.GetPgoPayload(PgoEntry)"/> to decode.
    /// The underlying value is the file offset (not RVA) of the payload start.
    /// </summary>
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
        /// Raw backward-reference delta to the deduplicated PGO data blob, or <see langword="null"/>
        /// when the data immediately follows the <c>versionAndFlags</c> value.
        /// </summary>
        public PgoDataBlobBackReferenceDelta? PgoDataBlobBackReferenceDelta { get; }

        internal PgoPayload(
            R2RSignature methodSignature,
            int pgoFormatVersion,
            PgoDataBlobBackReferenceDelta? pgoDataBlobBackReferenceDelta)
        {
            MethodSignature = methodSignature;
            PgoFormatVersion = pgoFormatVersion;
            PgoDataBlobBackReferenceDelta = pgoDataBlobBackReferenceDelta;
        }
    }
}
