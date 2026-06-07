using System;
using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.PreventSleep.Infrastructure;

internal sealed partial class MacosCfStringRef : SafeHandle
{
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public MacosCfStringRef() : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    private MacosCfStringRef(string value) : base(IntPtr.Zero, ownsHandle: true) =>
        SetHandle(CFStringCreateWithCharacters(nint.Zero, value, value.Length));

    public MacosCfStringRef(string value, int maxLength) : base(IntPtr.Zero, ownsHandle: true)
    {
        ArgumentNullException.ThrowIfNull(value);
        SetHandle(CFStringCreateWithCharacters(nint.Zero, value, Math.Min(value.Length, maxLength)));
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    public static implicit operator MacosCfStringRef(string value) => new(value);

    protected override bool ReleaseHandle()
    {
        CFRelease(handle);
        return true;
    }

    [LibraryImport(CoreFoundation)]
    private static partial nint CFStringCreateWithCharacters(
        nint alloc,
        [MarshalAs(UnmanagedType.LPWStr)] string chars,
        nint numChars);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint cf);
}
