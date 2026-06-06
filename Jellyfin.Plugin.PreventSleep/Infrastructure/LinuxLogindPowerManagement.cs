using System;
using Jellyfin.Plugin.PreventSleep.Interface;
using Microsoft.Win32.SafeHandles;

namespace Jellyfin.Plugin.PreventSleep.Infrastructure;

internal sealed class LinuxLogindPowerManagement : IPowerManagement
{
    private SafeFileHandle? _inhibitorHandle;

    public LinuxLogindPowerManagement()
    {
        LinuxDbusApi.MakeThreadSafe();
    }

    public void BlockSleep()
    {
        const string What = "sleep";
        const string Who = "Prevent Sleep Plugin for Jellyfin";
        const string Why = "Serving files/waiting for the configured amount of time for further requests";
        const string Mode = "block";

        var conn = nint.Zero;
        var msg = nint.Zero;
        var reply = nint.Zero;

        if (_inhibitorHandle is not null)
        {
            return;
        }

        LinuxDbusApi.dbus_error_init(out var error);
        try
        {
            conn = LinuxDbusApi.dbus_bus_get_private(LinuxDbusApi.DbusBusSystem, ref error);
            if (conn == nint.Zero)
            {
                throw new LinuxDbusException("Could not connect to system bus", in error);
            }

            LinuxDbusApi.dbus_connection_set_exit_on_disconnect(conn, false);

            msg = LinuxDbusApi.dbus_message_new_method_call(
                "org.freedesktop.login1",
                "/org/freedesktop/login1",
                "org.freedesktop.login1.Manager",
                "Inhibit");
            if (msg == nint.Zero)
            {
                throw new LinuxDbusException("Could not construct message (OOM?)");
            }

            LinuxDbusApi.dbus_message_iter_init_append(msg, out var appendIter);
            if (!LinuxDbusApi.dbus_message_iter_append_basic(ref appendIter, LinuxDbusApi.DbusTypeString, What) ||
                !LinuxDbusApi.dbus_message_iter_append_basic(ref appendIter, LinuxDbusApi.DbusTypeString, Who) ||
                !LinuxDbusApi.dbus_message_iter_append_basic(ref appendIter, LinuxDbusApi.DbusTypeString, Why) ||
                !LinuxDbusApi.dbus_message_iter_append_basic(ref appendIter, LinuxDbusApi.DbusTypeString, Mode))
            {
                throw new LinuxDbusException("Could not append args to message (OOM?)");
            }

            reply = LinuxDbusApi.dbus_connection_send_with_reply_and_block(conn, msg, -1, ref error);
            if (reply == nint.Zero)
            {
                if (LinuxDbusApi.dbus_error_has_name(
                        in error, LinuxDbusApi.DbusErrorInteractiveAuthorizationRequired))
                {
                    throw new UnauthorizedAccessException(
                        "Preventing sleep requires authentication. See https://github.com/jonschz/jellyfin-plugin-preventsleep/blob/main/docs/Linux.md");
                }

                throw new LinuxDbusException("Could not request inhibition", in error);
            }

            if (!LinuxDbusApi.dbus_message_iter_init(reply, out var replyIter))
            {
                throw new LinuxDbusException("Could not read arguments from reply");
            }

            if (LinuxDbusApi.dbus_message_iter_get_arg_type(in replyIter) is not LinuxDbusApi.DbusTypeUnixFd and var argType)
            {
                throw new LinuxDbusException($"Unexpected reply argument type {argType} (expected file descriptor)");
            }

            _inhibitorHandle = LinuxDbusApi.MessageIterGetFileDescriptor(ref replyIter);
        }
        finally
        {
            if (reply != nint.Zero)
            {
                LinuxDbusApi.dbus_message_unref(reply);
            }

            if (msg != nint.Zero)
            {
                LinuxDbusApi.dbus_message_unref(msg);
            }

            if (conn != nint.Zero)
            {
                LinuxDbusApi.dbus_connection_close(conn);
                LinuxDbusApi.dbus_connection_unref(conn);
            }

            if (LinuxDbusApi.dbus_error_is_set(in error))
            {
                LinuxDbusApi.dbus_error_free(ref error);
            }
        }
    }

    public void UnblockSleep()
    {
        _inhibitorHandle?.Close();
        _inhibitorHandle = null;
    }

    public void Dispose()
    {
        UnblockSleep();
    }
}
