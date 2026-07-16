// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Reflection.PortableExecutable;


namespace System.Reflection.Metadata.ReadyToRun;

public partial class ReadyToRunReader
{
    private readonly Dictionary<UnwindInfoRva, BaseGcInfo> _gcInfoCache = new();

    /// <summary>
    /// Resolve GC info for a runtime function identified by its <see cref="UnwindInfoRva"/>.
    /// GC info sits immediately after the unwind info in the image:
    /// - On I386: GcInfo offset == UnwindInfo offset (same location).
    /// - On other architectures: GcInfo offset == UnwindInfo offset + UnwindInfo.Size.
    /// </summary>
    /// <remarks>
    /// Crossgen2 emitter: per-method GC info blob attached to <c>MethodWithGCInfo</c> and aggregated by <c>RuntimeFunctionsGCInfoNode</c>.
    /// </remarks>
    public BaseGcInfo GetGcInfo(UnwindInfoRva handle)
    {
        EnsureSemanticDecodingSupported(nameof(GetGcInfo));

        if (_gcInfoCache.TryGetValue(handle, out BaseGcInfo cached))
            return cached;

        int gcInfoVersion = FormatProfile.GcInfoVersion;
        BaseGcInfo result;
        if (Machine == Machine.I386)
        {
            int gcInfoOffset = GetOffsetForRVA((int)handle);
            result = new x86.GcInfo(ImageReader, gcInfoOffset, gcInfoVersion);
        }
        else
        {
            BaseUnwindInfo unwindInfo = GetUnwindInfo(handle);
            int gcInfoRva = (int)handle + unwindInfo.Size;
            int gcInfoOffset = GetOffsetForRVA(gcInfoRva);
            result = new Amd64.GcInfo(ImageReader, gcInfoOffset, Machine, gcInfoVersion);
        }

        _gcInfoCache[handle] = result;
        return result;
    }
}
