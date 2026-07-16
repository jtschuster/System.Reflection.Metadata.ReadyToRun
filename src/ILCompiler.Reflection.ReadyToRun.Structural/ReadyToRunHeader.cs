// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Internal.ReadyToRunConstants;
using Internal.Runtime;

namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Fields common to the global R2R header and per-assembly headers in composite R2R images.
    /// </summary>
    public class ReadyToRunCoreHeader
    {
        /// <summary>Flags in the header.</summary>
        public uint Flags { get; }

        /// <summary>The ReadyToRun section handles.</summary>
        public IReadOnlyList<ReadyToRunSection> Sections { get; }

        public ReadyToRunCoreHeader(uint flags, IReadOnlyList<ReadyToRunSection> sections)
        {
            Flags = flags;
            Sections = sections;
        }
    }

    /// <summary>
    /// Structure representing the ReadyToRun header in a PE image.
    /// based on <a href="https://github.com/dotnet/runtime/blob/main/src/coreclr/inc/readytorun.h">src/inc/readytorun.h</a> READYTORUN_HEADER
    /// </summary>
    /// <remarks>
    /// Crossgen2 emitter: <c>ReadyToRunHeaderNode</c>.
    /// </remarks>
    public class ReadyToRunHeader
    {
        // READYTORUN_HEADER fields

        /// <summary>
        /// The expected signature of a ReadyToRun header
        /// </summary>
        public const uint READYTORUN_SIGNATURE = 0x00525452; // 'RTR'

        /// <summary>
        /// The highest R2R major version this reader has been validated against.
        /// Mirrors <c>READYTORUN_MAJOR_VERSION</c> in <c>src/coreclr/inc/readytorun.h</c>.
        /// Bump in lockstep with that constant when adding support for a newer format.
        /// </summary>
        public const ushort MAXIMUM_SUPPORTED_MAJOR_VERSION = 24;

        public uint Signature { get; }

        /// <summary>
        /// The ReadyToRun version
        /// </summary>
        public ushort MajorVersion { get; set; }
        public ushort MinorVersion { get; set; }

        /// <summary>Whether this reader has a semantic profile for the encoded version.</summary>
        public ReadyToRunFormatSupport FormatSupport { get; }

        // READYTORUN_CORE_HEADER fields

        /// <summary>
        /// Flags in the header
        /// eg. PLATFORM_NEUTRAL_SOURCE, SKIP_TYPE_VALIDATION
        /// </summary>
        public uint Flags { get; set; }

        /// <summary>
        /// The ReadyToRun section RVAs and sizes
        /// </summary>
        public IReadOnlyList<ReadyToRunSection> Sections { get; private set; }


        public ReadyToRunHeader(uint signature, ushort majorVersion, ushort minorVersion, uint flags, IReadOnlyList<ReadyToRunSection> sections)
        {
            Signature = signature;
            MajorVersion = majorVersion;
            MinorVersion = minorVersion;
            Flags = flags;
            Sections = sections;
            FormatSupport = ReadyToRunFormatProfile.Create(majorVersion, minorVersion).Support;
        }


        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Signature: 0x{Signature:X8} ('R2R')");
            // sb.AppendLine($"RelativeVirtualAddress: 0x{RelativeVirtualAddress:X8}");
            if (Signature == READYTORUN_SIGNATURE)
            {
                sb.AppendLine($"MajorVersion: 0x{MajorVersion:X4}");
                sb.AppendLine($"MinorVersion: 0x{MinorVersion:X4}");
                sb.AppendLine($"Flags: 0x{Flags:X8}");
                foreach (ReadyToRunFlags flag in Enum.GetValues(typeof(ReadyToRunFlags)))
                {
                    if ((Flags & (uint)flag) != 0)
                    {
                        sb.AppendLine($"  - {Enum.GetName(typeof(ReadyToRunFlags), flag)}");
                    }
                }
            }
            return sb.ToString();
        }
    }

    public partial class ReadyToRunReader
    {
        /// <summary>
        /// Reads a ReadyToRun header from the image at the given file offset.
        /// </summary>
        /// <param name="imageOffset">Index in the image byte array to the start of the ReadyToRun header</param>
        /// <exception cref="BadImageFormatException">The signature must be 0x00525452 ("RTR")</exception>
        public ReadyToRunHeader ReadReadyToRunHeader(int imageOffset)
        {
            try
            {
                uint signature = _nativeReader.ReadUInt32(ref imageOffset);
                if (signature != ReadyToRunHeader.READYTORUN_SIGNATURE)
                    throw new BadImageFormatException($"Incorrect R2R header signature: 0x{signature:X8}.");

                ushort majorVersion = _nativeReader.ReadUInt16(ref imageOffset);
                ushort minorVersion = _nativeReader.ReadUInt16(ref imageOffset);
                ReadyToRunFormatProfile profile = ReadyToRunFormatProfile.Create(majorVersion, minorVersion);
                if (ValidationMode == ReadyToRunValidationMode.Strict && !profile.IsSupported)
                {
                    throw new NotSupportedException(
                        $"ReadyToRun format version {majorVersion}.{minorVersion} is not a known CoreCLR format revision.");
                }

                ReadyToRunCoreHeader coreHeader = ReadReadyToRunCoreHeader(ref imageOffset, profile);
                return new ReadyToRunHeader(signature, majorVersion, minorVersion, coreHeader.Flags, coreHeader.Sections);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new BadImageFormatException(
                    "ReadyToRun header or section directory extends outside the image.", exception);
            }
            catch (EndOfStreamException exception)
            {
                throw new BadImageFormatException("ReadyToRun header or section directory is truncated.", exception);
            }
        }

        /// <summary>
        /// Reads the core header fields (flags + sections) shared by both the global header
        /// and per-assembly headers in composite R2R images.
        /// </summary>
        public ReadyToRunCoreHeader ReadReadyToRunCoreHeader(ref int curOffset)
            => ReadReadyToRunCoreHeader(ref curOffset, FormatProfile);

        private ReadyToRunCoreHeader ReadReadyToRunCoreHeader(
            ref int curOffset,
            ReadyToRunFormatProfile profile)
        {
            uint flags = _nativeReader.ReadUInt32(ref curOffset);
            profile.ValidateHeader(flags, ValidationMode);

            uint sectionCount = _nativeReader.ReadUInt32(ref curOffset);
            long directorySize = sectionCount * 12L;
            if (directorySize > _nativeReader.Length - curOffset || sectionCount > int.MaxValue)
                throw new BadImageFormatException("ReadyToRun section count exceeds the available directory data.");

            int sectionCountInt = (int)sectionCount;
            var sections = new List<ReadyToRunSection>(sectionCountInt);
            int previousType = int.MinValue;

            for (int i = 0; i < sectionCountInt; i++)
            {
                int type = _nativeReader.ReadInt32(ref curOffset);
                var sectionType = (ReadyToRunSectionType)type;
                if (ValidationMode == ReadyToRunValidationMode.Strict &&
                    !ReadyToRunFormatProfile.IsKnownCoreClrSectionType(type))
                {
                    throw new NotSupportedException($"ReadyToRun section type {type} is not a known CoreCLR section.");
                }

                if (type <= previousType)
                    throw new BadImageFormatException("ReadyToRun section directory must be strictly ordered by section type.");
                previousType = type;

                uint sectionStartRva = _nativeReader.ReadUInt32(ref curOffset);
                uint sectionLength = _nativeReader.ReadUInt32(ref curOffset);
                if (sectionStartRva > int.MaxValue || sectionLength > int.MaxValue)
                {
                    throw new NotSupportedException(
                        $"ReadyToRun section {type} exceeds the reader's 31-bit RVA or size range.");
                }

                var section = new ReadyToRunSection(sectionType, (ImageRVA)sectionStartRva, (int)sectionLength);
                ValidateAndGetSectionOffset(section);
                sections.Add(section);
            }
            return new ReadyToRunCoreHeader(flags, sections);
        }
    }
}
