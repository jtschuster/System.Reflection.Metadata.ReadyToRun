// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection.PortableExecutable;

namespace System.Reflection.Metadata.ReadyToRun
{
    /// <summary>
    /// WebAssembly does not have a dedicated <see cref="Machine"/> value, so ReadyToRun images
    /// produced for wasm targets use a placeholder. This mirrors <c>WasmMachine.Wasm32</c> in the
    /// runtime's <c>ILCompiler.Reflection.ReadyToRun</c>.
    /// </summary>
    public static class WasmMachine
    {
        /// <summary>Placeholder <see cref="Machine"/> value used for 32-bit WebAssembly images.</summary>
        public const Machine Wasm32 = (Machine)0xFFFE;
    }
}
