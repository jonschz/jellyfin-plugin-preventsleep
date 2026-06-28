using System;
using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.PreventSleep.Infrastructure;

internal sealed class LinuxDbusException(string message) : Exception(message)
{
    internal LinuxDbusException(string message, in LinuxDbusApi.DBusError error)
        : this(
            LinuxDbusApi.dbus_error_is_set(error) &&
            Marshal.PtrToStringUTF8(error.Message) is { } reason
                ? $"{message}: {reason}"
                : message)
    {
    }
}
