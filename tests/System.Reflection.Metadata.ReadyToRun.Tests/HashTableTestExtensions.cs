// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;

namespace System.Reflection.Metadata.ReadyToRun.Tests;

/// <summary>Test helpers that enumerate a hashtable and decode every entry.</summary>
internal static class HashTableTestExtensions
{
    public static List<AvailableTypeEntry> ReadAllAvailableTypeEntries(this ReadyToRunReader reader, AvailableTypesHashTableHandle table)
        => reader.EnumerateAvailableTypeEntries(table).Select(handle => reader.GetAvailableTypeEntry(table, handle)).ToList();

    public static List<ExternalTypeMapGroup> ReadAllExternalTypeMapGroups(this ReadyToRunReader reader, ExternalTypeMapsHashTableHandle table)
        => reader.EnumerateExternalTypeMapGroups(table).Select(handle => reader.GetExternalTypeMapGroup(table, handle)).ToList();

    public static List<ProxyTypeMapGroup> ReadAllProxyTypeMapGroups(this ReadyToRunReader reader, ProxyTypeMapsHashTableHandle table)
        => reader.EnumerateProxyTypeMapGroups(table).Select(handle => reader.GetProxyTypeMapGroup(table, handle)).ToList();

    public static List<TypeMapAssemblyTargetsEntry> ReadAllTypeMapAssemblyTargetsEntries(this ReadyToRunReader reader, TypeMapAssemblyTargetsHashTableHandle table)
        => reader.EnumerateTypeMapAssemblyTargetsEntries(table).Select(handle => reader.GetTypeMapAssemblyTargetsEntry(table, handle)).ToList();
}
