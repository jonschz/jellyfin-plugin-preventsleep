using System;
using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.PreventSleep.Infrastructure;

internal sealed partial class MacosIopmAssertion : SafeHandle
{
    private const string IoKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const int KIoReturnSuccess = 0;

    private MacosIopmAssertion(uint assertionId) : base(IntPtr.Zero, ownsHandle: true)
        => SetHandle((nint)assertionId);

    public override bool IsInvalid => handle == IntPtr.Zero; // 0 = kIOPMNullAssertionID

    protected override bool ReleaseHandle()
        => IOPMAssertionRelease((uint)(nuint)handle) == KIoReturnSuccess;

    internal static MacosIopmAssertion Create(
        MacosCfStringRef assertionType,
        MacosCfStringRef name,
        MacosCfStringRef details)
    {
        var result = IOPMAssertionCreateWithDescription(
            assertionType,
            name,
            details,
            nint.Zero,
            nint.Zero,
            0,
            nint.Zero,
            out var assertionId);

        return result == KIoReturnSuccess ? new MacosIopmAssertion(assertionId) : throw new MacosIoKitException(result);
    }

    [LibraryImport(IoKit)]
    private static partial int IOPMAssertionCreateWithDescription(
        MacosCfStringRef assertionType,
        MacosCfStringRef name,
        MacosCfStringRef details,
        nint humanReadableReason,
        nint localizationBundlePath,
        double timeout,
        nint timeoutAction,
        out uint assertionId);

    [LibraryImport(IoKit)]
    private static partial int IOPMAssertionRelease(uint assertionId);
}
