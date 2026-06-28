using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.PreventSleep.Infrastructure;

internal sealed class MacosIoKitException(int ioReturn)
    : ExternalException($"IOKit call failed (IOReturn = 0x{ioReturn:X8})", ioReturn);
