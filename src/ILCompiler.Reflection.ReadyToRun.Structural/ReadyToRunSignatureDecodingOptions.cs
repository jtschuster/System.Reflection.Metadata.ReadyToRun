// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

using Internal.ReadyToRunConstants;
using Internal.Runtime;

namespace System.Reflection.Metadata.ReadyToRun;

/// <summary>
/// Controls how signature and fixup decoding interprets ReadyToRun version-specific encodings.
/// Future compatibility/profile types can implement this interface without the decoder depending
/// on a concrete profile class.
/// </summary>
public interface IReadyToRunSignatureDecodingOptions
{
    ushort MajorVersion { get; }
    ushort MinorVersion { get; }
    ReadyToRunValidationMode ValidationMode { get; }
}

/// <summary>
/// Simple value object for callers that want to decode using an explicit ReadyToRun version/policy
/// without introducing a higher-level compatibility profile type.
/// </summary>
public readonly record struct ReadyToRunSignatureDecodingOptions(
    ushort MajorVersion,
    ushort MinorVersion,
    ReadyToRunValidationMode ValidationMode = ReadyToRunValidationMode.Tolerant) : IReadyToRunSignatureDecodingOptions
{
    public static ReadyToRunSignatureDecodingOptions Strict(ushort majorVersion, ushort minorVersion)
        => new(majorVersion, minorVersion, ReadyToRunValidationMode.Strict);

    public static ReadyToRunSignatureDecodingOptions Tolerant(ushort majorVersion, ushort minorVersion)
        => new(majorVersion, minorVersion, ReadyToRunValidationMode.Tolerant);
}

internal static class ReadyToRunSignatureCompatibility
{
    private const uint KnownMethodFlagMask =
        (uint)(ReadyToRunMethodSigFlags.READYTORUN_METHOD_SIG_UnboxingStub
        | ReadyToRunMethodSigFlags.READYTORUN_METHOD_SIG_InstantiatingStub
        | ReadyToRunMethodSigFlags.READYTORUN_METHOD_SIG_MethodInstantiation
        | ReadyToRunMethodSigFlags.READYTORUN_METHOD_SIG_SlotInsteadOfToken
        | ReadyToRunMethodSigFlags.READYTORUN_METHOD_SIG_MemberRefToken
        | ReadyToRunMethodSigFlags.READYTORUN_METHOD_SIG_Constrained
        | ReadyToRunMethodSigFlags.READYTORUN_METHOD_SIG_OwnerType
        | ReadyToRunMethodSigFlags.READYTORUN_METHOD_SIG_UpdateContext
        | ReadyToRunMethodSigFlags.READYTORUN_METHOD_SIG_AsyncVariant);

    private const uint KnownFieldFlagMask =
        (uint)(ReadyToRunFieldSigFlags.READYTORUN_FIELD_SIG_MemberRefToken
        | ReadyToRunFieldSigFlags.READYTORUN_FIELD_SIG_OwnerType);

    internal static ReadyToRunSignatureDecodingOptions CurrentStrict { get; } =
        new(ReadyToRunHeaderConstants.CurrentMajorVersion, ReadyToRunHeaderConstants.CurrentMinorVersion, ReadyToRunValidationMode.Strict);

    internal static ReadyToRunSignatureDecodingOptions FromHeader(
        ReadyToRunHeader header,
        ReadyToRunValidationMode validationMode)
        => new(header.MajorVersion, header.MinorVersion, validationMode);

    internal static void ValidateMethodFlags(uint flags, IReadyToRunSignatureDecodingOptions options)
    {
        uint unknownBits = flags & ~KnownMethodFlagMask;
        if (unknownBits != 0)
            throw Unsupported($"Method signature flags 0x{unknownBits:X} are not supported by the structural decoder.");

        ReadyToRunFormatProfile profile = GetProfile(options);
        RequireSupported(options,
            (flags & (uint)ReadyToRunMethodSigFlags.READYTORUN_METHOD_SIG_UpdateContext) != 0,
            profile.SupportsMethodSignatureUpdateContext,
            nameof(ReadyToRunMethodSigFlags.READYTORUN_METHOD_SIG_UpdateContext));
        RequireSupported(options,
            (flags & (uint)ReadyToRunMethodSigFlags.READYTORUN_METHOD_SIG_AsyncVariant) != 0,
            profile.SupportsMethodSignatureAsyncVariant,
            nameof(ReadyToRunMethodSigFlags.READYTORUN_METHOD_SIG_AsyncVariant));
    }

    internal static void ValidateFieldFlags(uint flags)
    {
        uint unknownBits = flags & ~KnownFieldFlagMask;
        if (unknownBits != 0)
            throw Unsupported($"Field signature flags 0x{unknownBits:X} are not supported by the structural decoder.");
    }

    internal static bool IsKnownFixupKind(ReadyToRunFixupKind fixupKind)
        => Enum.IsDefined(typeof(ReadyToRunFixupKind), fixupKind) && fixupKind != ReadyToRunFixupKind.ModuleOverride;

    internal static void ValidateFixupKind(ReadyToRunFixupKind fixupKind, IReadyToRunSignatureDecodingOptions options)
    {
        RequireSupported(
            options,
            present: true,
            GetProfile(options).SupportsFixupKind(fixupKind),
            fixupKind.ToString());
    }

    internal static bool IsKnownHelperId(uint helperId)
        => helperId <= (uint)ReadyToRunHelper.R2RToInterpreter
            && Enum.IsDefined(typeof(ReadyToRunHelper), (ReadyToRunHelper)helperId);

    internal static void ValidateHelperId(uint helperId, IReadyToRunSignatureDecodingOptions options)
    {
        ReadyToRunHelper helper = (ReadyToRunHelper)helperId;
        RequireSupported(
            options,
            present: true,
            GetProfile(options).SupportsHelper(helper),
            helper.ToString());
    }

    private static void RequireSupported(
        IReadyToRunSignatureDecodingOptions options,
        bool present,
        bool supported,
        string featureName)
    {
        if (!present || options.ValidationMode == ReadyToRunValidationMode.Tolerant)
            return;

        if (!supported)
        {
            throw Unsupported(
                $"{featureName} is not valid in ReadyToRun {options.MajorVersion}.{options.MinorVersion}.");
        }
    }

    private static ReadyToRunFormatProfile GetProfile(IReadyToRunSignatureDecodingOptions options)
        => ReadyToRunFormatProfile.Create(options.MajorVersion, options.MinorVersion);

    private static NotSupportedException Unsupported(string message) => new(message);
}
