// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Centralized interpretation of version-dependent CoreCLR ReadyToRun encodings.
    /// </summary>
    internal sealed class ReadyToRunFormatProfile
    {
        private const uint AllKnownHeaderFlags = 0x00000FFF;

        private ReadyToRunFormatProfile(ushort majorVersion, ushort minorVersion)
        {
            MajorVersion = majorVersion;
            MinorVersion = minorVersion;
            Support = IsKnownVersion(majorVersion, minorVersion)
                ? ReadyToRunFormatSupport.Supported
                : IsKnownMajorVersion(majorVersion)
                    ? ReadyToRunFormatSupport.CompatibleUnknownMinorVersion
                    : ReadyToRunFormatSupport.UnknownMajorVersion;
        }

        public ushort MajorVersion { get; }

        public ushort MinorVersion { get; }

        public ReadyToRunFormatSupport Support { get; }

        public bool IsSupported => Support == ReadyToRunFormatSupport.Supported;

        public bool CanDecodeKnownLayouts => Support != ReadyToRunFormatSupport.UnknownMajorVersion;

        public int ComponentAssemblyIndexOffset => IsAtLeast(6, 3) ? 2 : 1;

        public bool SupportsMethodSignatureUpdateContext => IsAtLeast(5, 4);

        public bool UsesPackedDebugBounds => IsAtLeast(16, 0);

        public bool UsesFatDebugInfo => IsAtLeast(17, 0);

        public int NativeVarInfoVersion
        {
            get
            {
                if (IsAtLeast(22, 0))
                    return 22;
                if (IsAtLeast(20, 0))
                    return 20;
                return 19;
            }
        }

        public int GcInfoVersion
        {
            get
            {
                if (MajorVersion == 1)
                    return 1;
                if (!IsAtLeast(9, 2))
                    return 2;
                if (!IsAtLeast(11, 0))
                    return 3;
                if (!IsAtLeast(21, 0))
                    return 4;
                return 5;
            }
        }

        public static ReadyToRunFormatProfile Create(ushort majorVersion, ushort minorVersion)
            => new ReadyToRunFormatProfile(majorVersion, minorVersion);

        public bool IsAtLeast(ushort majorVersion, ushort minorVersion)
            => MajorVersion > majorVersion || (MajorVersion == majorVersion && MinorVersion >= minorVersion);

        public void ValidateHeader(uint flags, ReadyToRunValidationMode validationMode)
        {
            if (validationMode != ReadyToRunValidationMode.Strict)
                return;

            if (!IsSupported)
            {
                throw new NotSupportedException(
                    $"ReadyToRun format version {MajorVersion}.{MinorVersion} is not a known CoreCLR format revision.");
            }

            uint unknownFlags = flags & ~AllKnownHeaderFlags;
            if (unknownFlags != 0)
            {
                throw new NotSupportedException(
                    $"ReadyToRun header contains unknown flag bits 0x{unknownFlags:X8}.");
            }
        }

        public void EnsureSemanticDecodingSupported(string operation)
        {
            if (!CanDecodeKnownLayouts)
            {
                throw new NotSupportedException(
                    $"{operation} is not supported for unknown ReadyToRun major version {MajorVersion}. " +
                    "Raw header, section-directory, and section-byte access remains available.");
            }
        }

        public static bool IsKnownCoreClrSectionType(int sectionType)
            => sectionType >= 100 && sectionType <= 126 && sectionType != 107;

        private static bool IsKnownVersion(ushort majorVersion, ushort minorVersion)
        {
            return majorVersion switch
            {
                1 => minorVersion == 2,
                2 => minorVersion <= 3,
                3 => minorVersion <= 2,
                4 => minorVersion is 1 or 2,
                5 => minorVersion is >= 1 and <= 4,
                6 => minorVersion <= 3,
                7 => minorVersion <= 1,
                8 => minorVersion == 0,
                9 => minorVersion <= 3,
                10 => minorVersion <= 1,
                11 or 12 => minorVersion == 0,
                13 => minorVersion <= 1,
                14 or 15 or 16 => minorVersion == 0,
                17 => minorVersion <= 1,
                18 => minorVersion <= 7,
                >= 19 and <= 24 => minorVersion == 0,
                _ => false,
            };
        }

        private static bool IsKnownMajorVersion(ushort majorVersion)
            => majorVersion is >= 1 and <= ReadyToRunHeader.MAXIMUM_SUPPORTED_MAJOR_VERSION;
    }
}
