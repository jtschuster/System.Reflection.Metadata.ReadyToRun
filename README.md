# Raw R2R Reader

Raw R2R Reader is a low-level ReadyToRun file format reader extracted from `dotnet/runtime`.
It exposes the ReadyToRun data close to the encoded structure and order used in the file.

## Projects

- `ILCompiler.Reflection.ReadyToRun.Structural` - the reader library.
- `ILCompiler.Reflection.ReadyToRun.Structural.Assertions` - assertion helpers used by parity and regression tests.

## Build

Install the .NET 11 SDK pinned in `global.json` (for example, with `dotnetup`), then run:

```bash
dotnetup install 11.0.100
dotnet build RawR2RReader.slnx
```

The stable .NET 11 SDK supplies C# 15, including the union language features used by
`ReadyToRunSection`; no preview language version or .NET 12 target is required. The reader retains
its existing 31-bit image-offset and section-length constraint because its underlying file offsets
are represented as `int`. Valid directory values remain available through the raw section fields.

## Test

```bash
dotnet test RawR2RReader.slnx
```

The tests publish a small Hello World app with ReadyToRun enabled, inspect the
result with `System.Reflection.Metadata.ReadyToRun`, and compare the parsed
header and section layout with R2RDump when `R2RDump` is available on `PATH` or
via `R2RDUMP_PATH`.

To also validate a real format 34 image, set `R2R_CURRENT_IMAGE_PATH` to an image produced by a
current Crossgen2 before running the tests.

## License

This project is derived from the .NET runtime repository and is licensed under the MIT license.
See [LICENSE.TXT](LICENSE.TXT) and [THIRD-PARTY-NOTICES.TXT](THIRD-PARTY-NOTICES.TXT).
