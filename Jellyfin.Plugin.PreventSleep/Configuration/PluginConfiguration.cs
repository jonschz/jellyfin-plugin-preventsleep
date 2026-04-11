using System;
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
    /// Gets or sets PreviousPowerScheme.
    /// <br/><br/>
    /// On Windows machines supporting Modern Standby, a custom power scheme must be used power scheme in order to
    /// block sleep correctly. We try to re-enable the previous power scheme when we unblock sleep, but this code is not
    /// guaranteed to run (e.g. if Jellyfin crashes while sleep is blocked). This value is different from <c>null</c>
    /// if and only if this plugin has activated a custom system power scheme. Therefore, if this value is non-null at
    /// startup, we know that the custom power scheme is still active from the last Jellyfin run and needs to be disabled.
    /// </summary>
    public Guid? PreviousPowerScheme { get; set; }
}
