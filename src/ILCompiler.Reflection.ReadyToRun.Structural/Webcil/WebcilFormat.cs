// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Reflection.Metadata.ReadyToRun.Webcil
{
    /// <summary>
    /// Constants describing the Webcil container format.
    /// Mirrors <c>WebcilConstants</c> in <c>src/coreclr/tools/Common/Wasm/Webcil.cs</c>.
    /// </summary>
    internal static class WebcilConstants
    {
        public const int WC_VERSION_MAJOR = 1;
        public const int WC_VERSION_MINOR = 0;

        /// <summary>
        /// 'WbIL' magic bytes interpreted as a little-endian uint32.
        /// </summary>
        public const uint WEBCIL_MAGIC = 0x4c496257;

        /// <summary>Size of a v0 Webcil header in bytes.</summary>
        public const int V0HeaderSize = 28;

        /// <summary>Size of a v1 Webcil header in bytes (adds <see cref="WebcilHeader.TableBase"/>).</summary>
        public const int V1HeaderSize = 32;

        /// <summary>Size of a single Webcil section header in bytes.</summary>
        public const int SectionHeaderSize = 16;
    }

    /// <summary>
    /// The header of a Webcil file. The header is a subset of the PE, COFF and CLI headers that are
    /// needed by the runtime to load managed assemblies.
    /// Mirrors <c>WebcilHeader</c> in <c>src/coreclr/tools/Common/Wasm/Webcil.cs</c>.
    /// </summary>
    internal struct WebcilHeader
    {
        public uint Id;
        public ushort VersionMajor;
        public ushort VersionMinor;

        public ushort CoffSections;
#pragma warning disable CS0649 // Reserved0 mirrors the on-disk layout but is never read.
        public ushort Reserved0;
#pragma warning restore CS0649

        public uint PeCliHeaderRva;
        public uint PeCliHeaderSize;

        public uint PeDebugRva;
        public uint PeDebugSize;

        // Present only in v1 headers.
        public uint TableBase;
    }

    /// <summary>
    /// Represents a section header in a Webcil file. This is the Webcil analog of
    /// <see cref="System.Reflection.PortableExecutable.SectionHeader"/>, but with fewer fields.
    /// </summary>
    internal readonly struct WebcilSectionHeader
    {
        public readonly uint VirtualSize;
        public readonly uint VirtualAddress;
        public readonly uint SizeOfRawData;
        public readonly uint PointerToRawData;

        public WebcilSectionHeader(uint virtualSize, uint virtualAddress, uint sizeOfRawData, uint pointerToRawData)
        {
            VirtualSize = virtualSize;
            VirtualAddress = virtualAddress;
            SizeOfRawData = sizeOfRawData;
            PointerToRawData = pointerToRawData;
        }
    }
}
