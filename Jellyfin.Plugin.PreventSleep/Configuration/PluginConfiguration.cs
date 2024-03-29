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
}
