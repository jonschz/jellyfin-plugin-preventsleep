using System;
using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.PreventSleep.Infrastructure;

internal sealed partial class MacosCfStringRef : SafeHandle
{
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    internal MacosCfStringRef(string value) : base(IntPtr.Zero, ownsHandle: true)
    {
        var ptr = CFStringCreateWithCharacters(nint.Zero, value, value.Length);
        if (ptr == nint.Zero)
        {
            throw new InvalidOperationException(
                $"Failed to create CFString from \"{value}\".");
        }

        SetHandle(ptr);
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

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
