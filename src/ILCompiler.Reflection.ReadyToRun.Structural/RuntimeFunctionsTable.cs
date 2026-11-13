// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Reflection.PortableExecutable;


namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Structural projection of the RuntimeFunctions section.
    /// A sorted array of runtime function entries, one per compiled code region.
    /// </summary>
    /// <remarks>
    /// Crossgen2 emitter: <c>RuntimeFunctionsTableNode</c>.
    /// </remarks>
    public sealed class RuntimeFunctionsTable
    {
        public IReadOnlyList<RuntimeFunctionEntry> Entries { get; }

        internal RuntimeFunctionsTable(List<RuntimeFunctionEntry> entries)
        {
            Entries = entries;
        }
    }

    public partial class ReadyToRunReader
    {
        /// <summary>
        /// Reads the runtime function table. Each entry's <see cref="RuntimeFunctionEntry.StartPCode"/>
        /// is an encoded code location, not always an RVA; on WebAssembly it is a virtual
        /// instruction-pointer index with bit 31 marking a funclet.
        /// </summary>
        public RuntimeFunctionsTable GetRuntimeFunctionsTable(ReadyToRunSection section)
        {
            int offset = ValidateAndGetSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.RuntimeFunctions,
                nameof(GetRuntimeFunctionsTable));
            int entrySize = CalculateRuntimeFunctionSize();
            if (section.Size % entrySize != 0)
            {
                throw new BadImageFormatException(
                    $"RuntimeFunctions section size {section.Size} is not divisible by entry size {entrySize}.");
            }

            int count = section.Size / entrySize;
            bool isAmd64 = Machine == Machine.Amd64;
            var entries = new List<RuntimeFunctionEntry>(count);

            for (int i = 0; i < count; i++)
            {
                var startPCode = (PCode)_nativeReader.ReadInt32(ref offset);
                CodeRva? endRva = null;
                if (isAmd64)
                    endRva = (CodeRva)_nativeReader.ReadInt32(ref offset);
                var unwindRva = (UnwindInfoRva)_nativeReader.ReadInt32(ref offset);
                entries.Add(new RuntimeFunctionEntry(startPCode, endRva, unwindRva));
            }

            return new RuntimeFunctionsTable(entries);
        }

        /// <summary>
        /// Calculates the runtime function entry size based on the target machine.
        /// Amd64 has 3 ints (start, end, unwind); others have 2 (start, unwind).
        /// </summary>
        internal int CalculateRuntimeFunctionSize()
        {
            return Machine == Machine.Amd64 ? 3 * sizeof(int) : 2 * sizeof(int);
        }

    }

    /// <summary>
    /// A single entry in the RuntimeFunctions table.
    /// </summary>
    public sealed class RuntimeFunctionEntry
    {
        /// <summary>
        /// Encoded code location for the start of the runtime function. It is usually an RVA;
        /// on ARM Thumb2 it includes the Thumb bit, and on WebAssembly it is a virtual
        /// instruction-pointer index with bit 31 marking a funclet.
        /// </summary>
        public PCode StartPCode { get; }

        /// <summary>RVA of the end of the code (Amd64 only; null on other architectures).</summary>
        public CodeRva? EndRva { get; }

        /// <summary>RVA of the unwind information.</summary>
        public UnwindInfoRva UnwindRva { get; }

        internal RuntimeFunctionEntry(PCode startPCode, CodeRva? endRva, UnwindInfoRva unwindRva)
        {
            StartPCode = startPCode;
            EndRva = endRva;
            UnwindRva = unwindRva;
        }
    }

}
