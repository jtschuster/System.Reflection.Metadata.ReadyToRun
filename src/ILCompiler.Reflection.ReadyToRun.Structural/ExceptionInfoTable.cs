// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;


namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Structural projection of the ExceptionInfo section.
    /// Each method entry maps a method RVA to its EH info RVA.
    /// </summary>
    /// <remarks>
    /// <code>
    /// repeated { method code RVA, EH info RVA }
    /// terminal { 0xFFFFFFFF, end RVA of the final method's EH data }
    /// </code>
    /// Crossgen2 emitter: <c>ExceptionInfoLookupTableNode</c>.
    /// </remarks>
    public sealed class ExceptionInfoTable
    {
        /// <summary>
        /// Records in image order. The final record is a sentinel, not a method entry:
        /// its method RVA is <c>0xFFFFFFFF</c>, and its EH info RVA marks the end of the
        /// last method's exception handling information.
        /// </summary>
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

            int totalRecords = length / recordSize;
            int realEntries = totalRecords - 1;

            for (int i = 0; i < realEntries; i++)
            {
                var methodRva = (CodeRva)_nativeReader.ReadInt32(ref offset);
                var ehInfoRva = (EHInfoRva)_nativeReader.ReadInt32(ref offset);
                entries.Add(new ExceptionInfoEntry(methodRva, ehInfoRva));
            }

            // The final record is a sentinel whose EH info RVA delimits the last method's clauses.
            int sentinelMethodRva = _nativeReader.ReadInt32(ref offset);
            var sentinelEhInfoRva = (EHInfoRva)_nativeReader.ReadInt32(ref offset);
            if (sentinelMethodRva != -1)
                throw new BadImageFormatException("ExceptionInfo section is missing its terminal sentinel record.");

            entries.Add(new ExceptionInfoEntry((CodeRva)sentinelMethodRva, sentinelEhInfoRva));
            return new ExceptionInfoTable(entries);
        }
    }

    /// <summary>
    /// A single entry in the ExceptionInfo table: maps a raw method code RVA
    /// to the RVA of its exception handling information. The final entry is a sentinel.
    /// </summary>
    /// <remarks>
    /// <code>
    /// uint32 method code RVA | uint32 EH info RVA
    /// </code>
    /// </remarks>
    public sealed class ExceptionInfoEntry
    {
        /// <summary>Raw RVA of the method code, or <c>0xFFFFFFFF</c> for the sentinel. This is not a PCode value.</summary>
        public CodeRva MethodRva { get; }

        /// <summary>RVA of the exception handling info, or its end RVA for the sentinel.</summary>
        public EHInfoRva EhInfoRva { get; }

        internal ExceptionInfoEntry(CodeRva methodRva, EHInfoRva ehInfoRva)
        {
            MethodRva = methodRva;
            EhInfoRva = ehInfoRva;
        }
    }

    /// <summary>Opaque handle representing an RVA pointing to exception handling information.</summary>
}
