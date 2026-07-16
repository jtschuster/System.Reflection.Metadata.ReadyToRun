// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Controls how the structural reader handles well-formed format values it does not recognize.
    /// </summary>
    public enum ReadyToRunValidationMode
    {
        /// <summary>
        /// Preserve unknown header values and section descriptors. Semantic decoders fail lazily
        /// when the image version or requested layout is not supported.
        /// </summary>
        Tolerant,

        /// <summary>
        /// Reject unknown versions and format discriminants while parsing their containing structure.
        /// </summary>
        Strict,
    }

    /// <summary>
    /// Options for <see cref="ReadyToRunReader"/>.
    /// </summary>
    public sealed class ReadyToRunReaderOptions
    {
        /// <summary>Shared tolerant options instance used when no options are supplied.</summary>
        public static ReadyToRunReaderOptions Default { get; } = new ReadyToRunReaderOptions();

        /// <summary>Gets the validation policy used by the reader.</summary>
        public ReadyToRunValidationMode ValidationMode { get; }

        public ReadyToRunReaderOptions(ReadyToRunValidationMode validationMode = ReadyToRunValidationMode.Tolerant)
        {
            ValidationMode = validationMode;
        }
    }

    /// <summary>
    /// Describes whether the reader has a semantic format profile for an R2R header version.
    /// Raw header and section-directory data remain available in tolerant mode for unknown versions.
    /// </summary>
    public enum ReadyToRunFormatSupport
    {
        /// <summary>The exact major/minor revision is known to this reader.</summary>
        Supported,

        /// <summary>
        /// The major version is supported but the minor revision is not in the historical ledger.
        /// Minor revisions are non-breaking, so tolerant mode can decode known layouts while
        /// preserving unknown discriminants.
        /// </summary>
        CompatibleUnknownMinorVersion,

        /// <summary>The major version has no semantic profile in this reader.</summary>
        UnknownMajorVersion,
    }
}
