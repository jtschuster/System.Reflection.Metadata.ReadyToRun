// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Opaque handle to a NativeFormat NativeArray stored in the image.
    /// </summary>
    public readonly struct NativeArrayHandle
    {
        internal int Offset { get; }

        /// <summary>
        /// Number of zero-based slots represented by the NativeArray.
        /// </summary>
        public int Count { get; }

        internal NativeArrayHandle(int offset, int count)
        {
            Offset = offset;
            Count = count;
        }
    }
}
