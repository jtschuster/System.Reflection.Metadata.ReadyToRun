// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace System.Reflection.Metadata.ReadyToRun;

internal static class ReadyToRunCollection
{
    internal static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var snapshot = new T[items.Count];
        for (int i = 0; i < snapshot.Length; i++)
            snapshot[i] = items[i];
        return Array.AsReadOnly(snapshot);
    }
}
