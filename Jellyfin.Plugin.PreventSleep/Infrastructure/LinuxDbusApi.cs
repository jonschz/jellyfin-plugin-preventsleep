using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Jellyfin.Plugin.PreventSleep.Infrastructure;

internal static partial class LinuxDbusApi
{
    private const string LibDbus = "libdbus-1.so.3";
    private static int _threadsInitialised;

    internal const int DbusBusSystem = 1;
    internal const int DbusTypeString = 's';
    internal const int DbusTypeUnixFd = 'h';

    internal const string DbusErrorInteractiveAuthorizationRequired =
        "org.freedesktop.DBus.Error.InteractiveAuthorizationRequired";

    internal static void MakeThreadSafe()
    {
        if (Interlocked.Exchange(ref _threadsInitialised, 1) == 0)
        {
            if (!dbus_threads_init_default())
            {
                throw new LinuxDbusException("Could not initialise D-BUS thread support (OOM?)");
            }
        }
    }

    internal static SafeFileHandle MessageIterGetFileDescriptor(ref DBusMessageIter iter)
    {
        // "Unix file descriptors that are read with this function will have the FD_CLOEXEC flag set."
        dbus_message_iter_get_basic(ref iter, out var fd);
        return new SafeFileHandle(fd, ownsHandle: true);
    }

#pragma warning disable SA1300
    [LibraryImport(LibDbus)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint dbus_bus_get_private(int busType, ref DBusError error);

    [LibraryImport(LibDbus)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void dbus_connection_close(nint connection);

    [LibraryImport(LibDbus)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint dbus_connection_send_with_reply_and_block(
        nint connection, nint message, int timeoutMs, ref DBusError error);

    [LibraryImport(LibDbus)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void dbus_connection_set_exit_on_disconnect(
        nint connection,
        [MarshalAs(UnmanagedType.U4)] bool exitOnDisconnect);

    [LibraryImport(LibDbus)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void dbus_connection_unref(nint connection);

    [LibraryImport(LibDbus)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void dbus_error_free(ref DBusError error);

    [LibraryImport(LibDbus, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U4)]
    internal static partial bool dbus_error_has_name(in DBusError error, string name);

    [LibraryImport(LibDbus)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void dbus_error_init(out DBusError error);

    [LibraryImport(LibDbus)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U4)]
    internal static partial bool dbus_error_is_set(in DBusError error);

    [LibraryImport(LibDbus)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U4)]
    internal static partial bool dbus_message_iter_append_basic(
        ref DBusMessageIter iter, int type, nint value);

    [LibraryImport(LibDbus)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U4)]
    internal static partial bool dbus_message_iter_append_basic(
        ref DBusMessageIter iter, int type,
        [MarshalAs(UnmanagedType.LPUTF8Str)] in string value);

    [LibraryImport(LibDbus)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int dbus_message_iter_get_arg_type(
        in DBusMessageIter iter);

    [LibraryImport(LibDbus)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void dbus_message_iter_get_basic(
        ref DBusMessageIter iter, out int value);

    [LibraryImport(LibDbus)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U4)]
    internal static partial bool dbus_message_iter_init(
        nint message, out DBusMessageIter iter);

    [LibraryImport(LibDbus)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void dbus_message_iter_init_append(
        nint message, out DBusMessageIter iter);

    [LibraryImport(LibDbus, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint dbus_message_new_method_call(
        string busName, string path, string iface, string method);

    [LibraryImport(LibDbus)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void dbus_message_unref(nint message);

    [LibraryImport(LibDbus)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U4)]
    private static partial bool dbus_threads_init_default();
#pragma warning restore SA1300

    [StructLayout(LayoutKind.Sequential)]
    internal struct DBusError
    {
        private nint _name;
        internal nint Message;
        private uint _flags;
        private nint _padding2;
    }

    [StructLayout(LayoutKind.Explicit, Size = 128)]
    internal struct DBusMessageIter;
}
