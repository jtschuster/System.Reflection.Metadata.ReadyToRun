using System.Diagnostics;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using Internal.Runtime;

using Xunit;
using Xunit.Abstractions;

namespace System.Reflection.Metadata.ReadyToRun.Tests;

public sealed class Crossgen2ReadyToRunTests
{
    private readonly ITestOutputHelper _output;

    public Crossgen2ReadyToRunTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Crossgen2HelloWorldCanBeInspected()
    {
        using TestReadyToRunImage image = TestReadyToRunImage.Create(_output);

        ReaderSummary summary = ReadyToRunInspector.Read(image.ReadyToRunAssemblyPath);

        Assert.Equal(ExpectedMachine(), summary.Machine);
        Assert.Equal(ReadyToRunHeader.READYTORUN_SIGNATURE, summary.Signature);
        Assert.NotEmpty(summary.CompilerIdentifier);
        Assert.True(summary.MajorVersion >= 1);
        Assert.Contains(ReadyToRunSectionType.RuntimeFunctions, summary.Sections.Keys);
        Assert.Contains(ReadyToRunSectionType.MethodDefEntryPoints, summary.Sections.Keys);
        Assert.Contains(ReadyToRunSectionType.AvailableTypes, summary.Sections.Keys);
        Assert.True(summary.RuntimeFunctionCount >= 1);
        Assert.True(summary.MethodDefEntryPointCount >= 1);
    }

    [Fact]
    public void HandleTypesDistinguishPCodeFromRva()
    {
        Assert.Equal(typeof(PCode), typeof(RuntimeFunctionEntry).GetProperty(nameof(RuntimeFunctionEntry.StartPCode))?.PropertyType);
        Assert.Null(typeof(RuntimeFunctionEntry).GetProperty("Index"));
        Assert.Equal(typeof(CodeRva?), typeof(RuntimeFunctionEntry).GetProperty(nameof(RuntimeFunctionEntry.EndRva))?.PropertyType);
        Assert.Equal(typeof(CodeRva), typeof(ExceptionInfoEntry).GetProperty(nameof(ExceptionInfoEntry.MethodRva))?.PropertyType);
        Assert.Equal(typeof(DelayLoadMethodThunkRva), typeof(ReadyToRunSection).GetProperty(nameof(ReadyToRunSection.DelayLoadMethodThunkRva))?.PropertyType);
    }

    private static Machine ExpectedMachine()
        => RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => Machine.Amd64,
            Architecture.X86 => Machine.I386,
            Architecture.Arm => Machine.Arm,
            Architecture.Arm64 => Machine.Arm64,
            _ => throw new PlatformNotSupportedException($"Unsupported architecture: {RuntimeInformation.ProcessArchitecture}")
        };

    [R2RDumpFact]
    public void ReaderHeaderAndSectionsAlignWithR2RDump()
    {
        using TestReadyToRunImage image = TestReadyToRunImage.Create(_output);
        ReaderSummary readerSummary = ReadyToRunInspector.Read(image.ReadyToRunAssemblyPath);

        string r2rDumpPath = R2RDumpLocator.FindRequired();
        CommandResult r2rDump = CommandRunner.Run(
            r2rDumpPath,
            ["--header", "-i", image.ReadyToRunAssemblyPath],
            image.WorkingDirectory);
        R2RDumpSummary r2rDumpSummary = R2RDumpSummary.Parse(r2rDump.StandardOutput);

        Assert.Equal(readerSummary.Machine.ToString(), r2rDumpSummary.Machine);
        Assert.Equal(readerSummary.MajorVersion, r2rDumpSummary.MajorVersion);
        Assert.Equal(readerSummary.MinorVersion, r2rDumpSummary.MinorVersion);
        Assert.Equal(readerSummary.Sections.Count, r2rDumpSummary.SectionCount);
        Assert.Equal(readerSummary.MethodDefEntryPointCount, r2rDumpSummary.MethodCount);

        foreach ((ReadyToRunSectionType type, SectionSummary readerSection) in readerSummary.Sections)
        {
            SectionSummary r2rDumpSection = Assert.Contains(type.ToString(), r2rDumpSummary.Sections);
            Assert.Equal(readerSection.RelativeVirtualAddress, r2rDumpSection.RelativeVirtualAddress);
            Assert.Equal(readerSection.Size, r2rDumpSection.Size);
        }
    }
}

internal sealed class R2RDumpFactAttribute : FactAttribute
{
    public R2RDumpFactAttribute()
    {
        if (!R2RDumpLocator.TryFind(out _, out string? skipReason))
        {
            Skip = skipReason;
        }
    }
}

internal sealed class TestReadyToRunImage : IDisposable
{
    private TestReadyToRunImage(string workingDirectory, string readyToRunAssemblyPath)
    {
        WorkingDirectory = workingDirectory;
        ReadyToRunAssemblyPath = readyToRunAssemblyPath;
    }

    public string WorkingDirectory { get; }

    public string ReadyToRunAssemblyPath { get; }

    public static TestReadyToRunImage Create(ITestOutputHelper output)
    {
        string workingDirectory = Path.Combine(Path.GetTempPath(), "r2r-reader-tests", Guid.NewGuid().ToString("N"));
        string projectDirectory = Path.Combine(workingDirectory, "HelloWorld");
        string publishDirectory = Path.Combine(workingDirectory, "publish");
        Directory.CreateDirectory(projectDirectory);

        File.WriteAllText(
            Path.Combine(projectDirectory, "HelloWorld.csproj"),
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
        File.WriteAllText(
            Path.Combine(projectDirectory, "Program.cs"),
            """
            Console.WriteLine("Hello, ReadyToRun!");
            """);

        string rid = RuntimeInformation.RuntimeIdentifier;
        CommandResult publish = CommandRunner.Run(
            "dotnet",
            [
                "publish",
                Path.Combine(projectDirectory, "HelloWorld.csproj"),
                "-c",
                "Release",
                "-r",
                rid,
                "--self-contained",
                "false",
                "-p:PublishReadyToRun=true",
                "-p:DebugType=none",
                "-o",
                publishDirectory,
                "--nologo"
            ],
            workingDirectory);
        output.WriteLine(publish.StandardOutput);

        string readyToRunAssemblyPath = Path.Combine(publishDirectory, "HelloWorld.dll");
        Assert.True(File.Exists(readyToRunAssemblyPath), $"Expected publish output at {readyToRunAssemblyPath}");

        return new TestReadyToRunImage(workingDirectory, readyToRunAssemblyPath);
    }

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

internal static class ReadyToRunInspector
{
    public static ReaderSummary Read(string assemblyPath)
    {
        Stream peStream = File.OpenRead(assemblyPath);
        Stream imageStream = File.OpenRead(assemblyPath);
        using PEReader peReader = new PEReader(peStream);
        using ReadyToRunReader reader = new ReadyToRunReader(
            new PEImageReader(peReader),
            new NativeReader(imageStream, leaveOpen: false),
            assemblyPath);

        ReadyToRunHeader header = reader.ReadyToRunHeader;
        Dictionary<ReadyToRunSectionType, SectionSummary> sections = header.Sections.ToDictionary(
            section => section.Type,
            section => new SectionSummary((int)section.RelativeVirtualAddress, section.Size));

        int runtimeFunctionCount = 0;
        if (sections.ContainsKey(ReadyToRunSectionType.RuntimeFunctions))
        {
            ReadyToRunSection section = header.Sections.Single(section => section.Type == ReadyToRunSectionType.RuntimeFunctions);
            runtimeFunctionCount = reader.GetRuntimeFunctionsTable(section).Entries.Count;
        }

        int methodDefEntryPointCount = 0;
        if (sections.ContainsKey(ReadyToRunSectionType.MethodDefEntryPoints))
        {
            ReadyToRunSection section = header.Sections.Single(section => section.Type == ReadyToRunSectionType.MethodDefEntryPoints);
            MethodDefEntryPointsTable table = reader.GetMethodDefEntryPointsTable(section);
            foreach (var unused in reader.EnumerateMethodDefEntryPoints(table))
                methodDefEntryPointCount++;
        }

        string compilerIdentifier = string.Empty;
        if (sections.ContainsKey(ReadyToRunSectionType.CompilerIdentifier))
        {
            ReadyToRunSection section = header.Sections.Single(section => section.Type == ReadyToRunSectionType.CompilerIdentifier);
            compilerIdentifier = reader.GetCompilerIdentifier(section);
        }

        return new ReaderSummary(
            reader.Machine,
            header.Signature,
            header.MajorVersion,
            header.MinorVersion,
            compilerIdentifier,
            sections,
            runtimeFunctionCount,
            methodDefEntryPointCount);
    }
}

internal sealed record ReaderSummary(
    Machine Machine,
    uint Signature,
    ushort MajorVersion,
    ushort MinorVersion,
    string CompilerIdentifier,
    IReadOnlyDictionary<ReadyToRunSectionType, SectionSummary> Sections,
    int RuntimeFunctionCount,
    int MethodDefEntryPointCount);

internal sealed record SectionSummary(int RelativeVirtualAddress, int Size);

internal sealed record R2RDumpSummary(
    string Machine,
    ushort MajorVersion,
    ushort MinorVersion,
    int SectionCount,
    int MethodCount,
    IReadOnlyDictionary<string, SectionSummary> Sections)
{
    public static R2RDumpSummary Parse(string output)
    {
        string machine = MatchRequired(output, @"^Machine:\s+(?<value>\w+)$", RegexOptions.Multiline);
        ushort majorVersion = ParseHexUInt16(MatchRequired(output, @"^MajorVersion:\s+0x(?<value>[0-9A-Fa-f]+)$", RegexOptions.Multiline));
        ushort minorVersion = ParseHexUInt16(MatchRequired(output, @"^MinorVersion:\s+0x(?<value>[0-9A-Fa-f]+)$", RegexOptions.Multiline));
        int sectionCount = int.Parse(MatchRequired(output, @"^(?<value>\d+)\s+sections$", RegexOptions.Multiline), CultureInfo.InvariantCulture);
        int methodCount = int.Parse(MatchRequired(output, @"^(?<value>\d+)\s+methods$", RegexOptions.Multiline), CultureInfo.InvariantCulture);

        Dictionary<string, SectionSummary> sections = new Dictionary<string, SectionSummary>(StringComparer.Ordinal);
        string? currentSection = null;
        int? currentRva = null;

        foreach (string line in output.Split('\n'))
        {
            Match typeMatch = Regex.Match(line, @"^Type:\s+(?<name>\w+)\s+\(\d+\)\s*$");
            if (typeMatch.Success)
            {
                currentSection = typeMatch.Groups["name"].Value;
                currentRva = null;
                continue;
            }

            if (currentSection is null)
            {
                continue;
            }

            Match rvaMatch = Regex.Match(line, @"^RelativeVirtualAddress:\s+0x(?<value>[0-9A-Fa-f]+)\s*$");
            if (rvaMatch.Success)
            {
                currentRva = int.Parse(rvaMatch.Groups["value"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                continue;
            }

            Match sizeMatch = Regex.Match(line, @"^Size:\s+(?<value>\d+)\s+bytes\s*$");
            if (sizeMatch.Success && currentRva.HasValue)
            {
                int size = int.Parse(sizeMatch.Groups["value"].Value, CultureInfo.InvariantCulture);
                sections.Add(currentSection, new SectionSummary(currentRva.Value, size));
                currentSection = null;
                currentRva = null;
            }
        }

        return new R2RDumpSummary(machine, majorVersion, minorVersion, sectionCount, methodCount, sections);
    }

    private static string MatchRequired(string text, string pattern, RegexOptions options)
    {
        Match match = Regex.Match(text, pattern, options);
        Assert.True(match.Success, $"Could not find pattern '{pattern}' in R2RDump output:{Environment.NewLine}{text}");
        return match.Groups["value"].Value;
    }

    private static ushort ParseHexUInt16(string value)
        => ushort.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}

internal static class R2RDumpLocator
{
    public static string FindRequired()
    {
        if (!TryFind(out string? path, out string? reason))
        {
            throw new InvalidOperationException(reason);
        }

        return path ?? throw new InvalidOperationException("R2RDump path resolution succeeded without a path.");
    }

    public static bool TryFind(out string? path, out string? skipReason)
    {
        string? configuredPath = Environment.GetEnvironmentVariable("R2RDUMP_PATH");
        if (TryResolveCandidate(configuredPath, out path))
        {
            skipReason = null;
            return true;
        }

        if (TryFindOnPath(out path))
        {
            skipReason = null;
            return true;
        }

        if (TryFindInRuntimeArtifacts(out path))
        {
            skipReason = null;
            return true;
        }

        skipReason = "R2RDump was not found. Set R2RDUMP_PATH to run R2RDump parity tests.";
        return false;
    }

    private static bool TryResolveCandidate(string? candidate, out string? path)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (File.Exists(candidate))
        {
            path = candidate;
            return true;
        }

        if (Directory.Exists(candidate))
        {
            string executableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "R2RDump.exe" : "R2RDump";
            string nested = Path.Combine(candidate, executableName);
            if (File.Exists(nested))
            {
                path = nested;
                return true;
            }
        }

        return false;
    }

    private static bool TryFindOnPath(out string? path)
    {
        path = null;
        string executableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "R2RDump.exe" : "R2RDump";
        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (pathVariable is null)
        {
            return false;
        }

        foreach (string directory in pathVariable.Split(Path.PathSeparator))
        {
            if (TryResolveCandidate(Path.Combine(directory, executableName), out path))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFindInRuntimeArtifacts(out string? path)
    {
        path = null;
        string? home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            return false;
        }

        string sourceRoot = Path.Combine(home, "src");
        if (!Directory.Exists(sourceRoot))
        {
            return false;
        }

        IEnumerable<string> runtimeRoots = Directory.EnumerateDirectories(sourceRoot, "runtime*");
        foreach (string runtimeRoot in runtimeRoots)
        {
            string artifactsRoot = Path.Combine(runtimeRoot, "artifacts", "bin", "coreclr");
            if (!Directory.Exists(artifactsRoot))
            {
                continue;
            }

            foreach (string candidate in Directory.EnumerateFiles(artifactsRoot, "R2RDump", SearchOption.AllDirectories))
            {
                if (TryResolveCandidate(candidate, out path))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

internal static class CommandRunner
{
    public static CommandResult Run(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start {fileName}.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{fileName} exited with code {process.ExitCode}.{Environment.NewLine}" +
                $"stdout:{Environment.NewLine}{standardOutput}{Environment.NewLine}" +
                $"stderr:{Environment.NewLine}{standardError}");
        }

        return new CommandResult(standardOutput, standardError);
    }
}

internal sealed record CommandResult(string StandardOutput, string StandardError);
