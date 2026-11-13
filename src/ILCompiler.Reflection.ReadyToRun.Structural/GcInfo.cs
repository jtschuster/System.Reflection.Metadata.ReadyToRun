// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Reflection.PortableExecutable;


namespace System.Reflection.Metadata.ReadyToRun;

public partial class ReadyToRunReader
{
    /// <summary>
    /// Resolve GC info for a runtime function identified by its <see cref="UnwindInfoRva"/>.
    /// GC info sits immediately after the unwind info in the image:
    /// - On I386: GcInfo offset == UnwindInfo offset (same location).
    /// - On other architectures: GcInfo offset == UnwindInfo offset + UnwindInfo.Size.
    /// </summary>
    /// <remarks>
    /// Crossgen2 emitter: per-method GC info blob attached to <c>MethodWithGCInfo</c> and aggregated by <c>RuntimeFunctionsGCInfoNode</c>.
    /// </remarks>
    internal BaseGcInfo GetGcInfo(UnwindInfoRva handle)
    {
        EnsureSemanticDecodingSupported(nameof(GetGcInfo));

        int gcInfoVersion = FormatProfile.GcInfoVersion;
        BaseGcInfo result;
        if (Machine == Machine.I386)
        {
            int gcInfoOffset = (int)GetOffsetForRVA((ImageRVA)handle.Value);
            result = new x86.GcInfo(_nativeReader, gcInfoOffset, gcInfoVersion);
        }
        else
        {
            BaseUnwindInfo unwindInfo = GetUnwindInfo(handle);
            int gcInfoRva = (int)handle + unwindInfo.Size;
            int gcInfoOffset = (int)GetOffsetForRVA((ImageRVA)gcInfoRva);
            result = new Amd64.GcInfo(_nativeReader, gcInfoOffset, Machine, gcInfoVersion);
        }

        return result;
    }
}
