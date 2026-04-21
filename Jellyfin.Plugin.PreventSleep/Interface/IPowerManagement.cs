namespace Jellyfin.Plugin.PreventSleep.Interface;

public interface IPowerManagement
{
    public void BlockSleep();

    public void UnblockSleep();
}
