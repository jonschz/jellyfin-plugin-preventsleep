using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PreventSleep.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        // set default options here
        UnblockSleepDelay = 1;
    }

    /// <summary>
    /// Gets or sets the amount of minutes to wait before re-enabling sleep.
    /// </summary>
    public int UnblockSleepDelay { get; set; }

    /// <summary>
    /// Gets or sets ACTimeoutToBeRestoredAtStartup.
    /// <br/><br/>
    /// On Windows machines supporting Modern Standby, small but persistent changes need to be made to the power scheme
    /// in order to block sleep correctly. We try to revert these changes when we unblock sleep, but this code is not
    /// guaranteed to run (e.g. if Jellyfin crashes while sleep is blocked). This value is different from <c>null</c>
    /// if and only if the system value is currently modified. Therefore, if this value is non-null at startup, we know
    /// that the value is still modified from the last Jellyfin run and needs to be reverted.
    /// <br/>
    /// </summary>
    /// <seealso cref="WindowsPowerManagement"/>
    public uint? ACTimeoutToBeRestoredAtStartup { get; set; }

    /// <summary>
    /// Gets or sets DCTimeoutToBeRestoredAtStartup. See <see cref="ACTimeoutToBeRestoredAtStartup"/>.
    /// </summary>
    public uint? DCTimeoutToBeRestoredAtStartup { get; set; }
}
