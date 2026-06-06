using System;

namespace Jellyfin.Plugin.PreventSleep.Interface;

public interface IPowerManagement : IDisposable
{
    public void BlockSleep();

    public void UnblockSleep();
}
