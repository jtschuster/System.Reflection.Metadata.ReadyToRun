// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace System.Reflection.Metadata.ReadyToRun.Tests;

/// <summary>
/// xunit class fixture that publishes a single ReadyToRun image rich in generic instantiations
/// (so the InstanceMethodEntryPoints section is populated) and shares it across every test in a
/// class. Publishing is expensive, so it happens exactly once per test class.
/// </summary>
public sealed class GenericsR2RImageFixture : IDisposable
{
    public string WorkingDirectory { get; }

    public string AssemblyPath { get; }

    public GenericsR2RImageFixture()
    {
        WorkingDirectory = Path.Combine(Path.GetTempPath(), "r2r-reader-tests", "generics", Guid.NewGuid().ToString("N"));
        string projectDirectory = Path.Combine(WorkingDirectory, "Generics");
        string publishDirectory = Path.Combine(WorkingDirectory, "publish");
        Directory.CreateDirectory(projectDirectory);

        File.WriteAllText(
            Path.Combine(projectDirectory, "Generics.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net11.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);

        // App-local generic type and generic method, instantiated over many distinct value types
        // (value-type instantiations are not canonical-shared, so each is a distinct entry) plus a
        // few reference types. This drives the InstanceMethodEntryPoints NativeHashtable well past
        // the writer's 13-entries-per-bucket fill factor so it has multiple buckets.
        File.WriteAllText(
            Path.Combine(projectDirectory, "Program.cs"),
            """
            using System.Runtime.CompilerServices;

            public sealed class Box<T>
            {
                private readonly T _value;
                public Box(T value) => _value = value;

                [MethodImpl(MethodImplOptions.NoInlining)]
                public T Get() => _value;

                [MethodImpl(MethodImplOptions.NoInlining)]
                public bool IsDefault() => System.Collections.Generic.EqualityComparer<T>.Default.Equals(_value, default!);
            }

            struct S0 { public int A; }
            struct S1 { public long A; }
            struct S2 { public byte A; public byte B; }
            struct S3 { public int A; public int B; }
            struct S4 { public double A; }
            struct S5 { public short A; public short B; public short C; }
            struct S6 { public long A; public long B; }
            struct S7 { public float A; public int B; }

            static class Program
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                static U Echo<U>(U value) => value;

                [MethodImpl(MethodImplOptions.NoInlining)]
                static long Work<T>(T value)
                {
                    var box = new Box<T>(value);
                    T echoed = Echo<T>(value);
                    long result = box.IsDefault() ? 1 : 0;
                    result += box.Get()!.GetHashCode();
                    result += echoed!.GetHashCode();
                    return result;
                }

                static int Main()
                {
                    long total = 0;
                    total += Work<int>(1);
                    total += Work<uint>(2);
                    total += Work<long>(3);
                    total += Work<ulong>(4);
                    total += Work<byte>(5);
                    total += Work<sbyte>(6);
                    total += Work<short>(7);
                    total += Work<ushort>(8);
                    total += Work<char>('a');
                    total += Work<bool>(true);
                    total += Work<float>(1.5f);
                    total += Work<double>(2.5);
                    total += Work<S0>(default);
                    total += Work<S1>(default);
                    total += Work<S2>(default);
                    total += Work<S3>(default);
                    total += Work<S4>(default);
                    total += Work<S5>(default);
                    total += Work<S6>(default);
                    total += Work<S7>(default);
                    total += Work<string>("ref");
                    total += Work<object>(new object());
                    System.Console.WriteLine(total);
                    return 0;
                }
            }
            """);

        string rid = RuntimeInformation.RuntimeIdentifier;
        CommandRunner.Run(
            "dotnet",
            [
                "publish",
                Path.Combine(projectDirectory, "Generics.csproj"),
                "-c", "Release",
                "-r", rid,
                "--self-contained", "false",
                "-p:PublishReadyToRun=true",
                "-p:DebugType=none",
                "-o", publishDirectory,
                "--nologo"
            ],
            WorkingDirectory);

        AssemblyPath = Path.Combine(publishDirectory, "Generics.dll");
        if (!File.Exists(AssemblyPath))
        {
            throw new InvalidOperationException($"Expected publish output at {AssemblyPath}.");
        }
    }

    /// <summary>Open a live reader over the published image. The caller disposes the returned scope.</summary>
    public ReaderScope OpenReader() => ReaderScope.Open(AssemblyPath);

    public void Dispose()
    {
        try
        {
            Directory.Delete(WorkingDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// Owns the disposable graph behind a live <see cref="ReadyToRunReader"/> opened from a file so a
/// test can <c>using</c> it.
/// </summary>
public sealed class ReaderScope : IDisposable
{
    private readonly FileStream _peStream;

    private ReaderScope(FileStream peStream, PEReader peReader, ReadyToRunReader reader)
    {
        _peStream = peStream;
        PEReader = peReader;
        Reader = reader;
    }

    public PEReader PEReader { get; }

    public ReadyToRunReader Reader { get; }

    public static ReaderScope Open(string assemblyPath)
    {
        FileStream peStream = File.OpenRead(assemblyPath);
        FileStream imageStream = File.OpenRead(assemblyPath);
        PEReader peReader = new PEReader(peStream);
        ReadyToRunReader reader = new ReadyToRunReader(
            new PEImageReader(peReader),
            new NativeReader(imageStream, leaveOpen: false),
            assemblyPath);
        return new ReaderScope(peStream, peReader, reader);
    }

    public void Dispose()
    {
        Reader.Dispose();
        PEReader.Dispose();
        _peStream.Dispose();
    }
}
