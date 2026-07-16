// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;


namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Structural projection of the ExceptionInfo section.
    /// Each entry maps a method RVA to its EH info RVA.
    /// </summary>
    /// <remarks>
    /// Crossgen2 emitter: <c>ExceptionInfoLookupTableNode</c>.
    /// </remarks>
    public sealed class ExceptionInfoTable
    {
        public IReadOnlyList<ExceptionInfoEntry> Entries { get; }

        internal ExceptionInfoTable(List<ExceptionInfoEntry> entries)
        {
            Entries = entries;
        }
    }

    public partial class ReadyToRunReader
    {
        public ExceptionInfoTable GetExceptionInfoTable(ReadyToRunSection section)
        {
            int offset = ValidateAndGetSectionOffset(
                section,
                Internal.Runtime.ReadyToRunSectionType.ExceptionInfo,
                nameof(GetExceptionInfoTable));
            int length = section.Size;
            const int recordSize = 2 * sizeof(int);
            if (length < recordSize || length % recordSize != 0)
            {
                throw new BadImageFormatException(
                    $"ExceptionInfo section size {length} does not contain a whole sentinel-terminated record array.");
            }

            var entries = new List<ExceptionInfoEntry>();

            // The encoding ends with a sentinel record (MethodRva = ~0u, EhInfoRva = endOfEhInfo)
            // used to compute the size of the previous record's clauses. It is not a real entry.
            int totalRecords = length / recordSize;
            int realEntries = totalRecords - 1;

            for (int i = 0; i < realEntries; i++)
            {
                var methodRva = (CodeRva)_nativeReader.ReadInt32(ref offset);
                var ehInfoRva = (EHInfoRva)_nativeReader.ReadInt32(ref offset);
                entries.Add(new ExceptionInfoEntry(methodRva, ehInfoRva));
            }

            int sentinelMethodRva = _nativeReader.ReadInt32(ref offset);
            _nativeReader.ReadInt32(ref offset);
            if (sentinelMethodRva != -1)
                throw new BadImageFormatException("ExceptionInfo section is missing its terminal sentinel record.");

            return new ExceptionInfoTable(entries);
        }
    }

    /// <summary>
    /// A single entry in the ExceptionInfo table: maps a raw method code RVA
    /// to the RVA of its exception handling information.
    /// </summary>
    public sealed class ExceptionInfoEntry
    {
        /// <summary>Raw RVA of the method code. This is not a PCode value.</summary>
        public CodeRva MethodRva { get; }

        /// <summary>RVA of the exception handling info.</summary>
        public EHInfoRva EhInfoRva { get; }

        internal ExceptionInfoEntry(CodeRva methodRva, EHInfoRva ehInfoRva)
        {
            MethodRva = methodRva;
            EhInfoRva = ehInfoRva;
        }
    }

    /// <summary>Opaque handle representing an RVA pointing to exception handling information.</summary>
    public enum EHInfoRva : uint {}
}
