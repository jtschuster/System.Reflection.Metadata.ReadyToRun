# Raw R2R Reader

Raw R2R Reader is a low-level ReadyToRun file format reader extracted from `dotnet/runtime`.
It exposes the ReadyToRun data close to the encoded structure and order used in the file.

## Projects

- `ILCompiler.Reflection.ReadyToRun.Structural` - the reader library.
- `ILCompiler.Reflection.ReadyToRun.Structural.Assertions` - assertion helpers used by parity and regression tests.

## Build

Install a .NET 11 SDK, then run:

```bash
dotnet build RawR2RReader.slnx
```

## License

This project is derived from the .NET runtime repository and is licensed under the MIT license.
See [LICENSE.TXT](LICENSE.TXT) and [THIRD-PARTY-NOTICES.TXT](THIRD-PARTY-NOTICES.TXT).
