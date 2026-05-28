// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// Strong type for a MethodDef row ID (1-based).
    /// Cast to/from <see cref="int"/> as needed when interoperating with
    /// <see cref="System.Reflection.Metadata.MetadataReader"/> APIs.
    /// </summary>
    public enum MethodRid : uint {}
}
