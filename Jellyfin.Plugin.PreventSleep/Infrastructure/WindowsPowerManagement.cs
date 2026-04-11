using System;
using System.Collections.Generic;
using System.ComponentModel;
using Jellyfin.Plugin.PreventSleep.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;

namespace Jellyfin.Plugin.PreventSleep.Infrastructure;

public sealed class WindowsPowerManagement : IPowerManagement
{
    private const string JellyfinPowerSchemeName = "Temporary Jellyfin Scheme";
    private const string JellyfinPowerSchemeDescription = "Temporary power scheme created by jellyfin-plugin-preventsleep to prevent unexpected shutdowns. Will be deleted automatically when no longer used.";
    private readonly ILogger<WindowsPowerManagement> _logger;
    private readonly SafeFileHandle _powerRequest;

    /// <summary>
    /// See https://stackoverflow.com/a/23505373 why special handling regarding Modern Standby is needed.
    /// </summary>
    private readonly bool _supportsModernStandby;
    private readonly Plugin _plugin;
    private readonly WindowsPowerApi _windowsPowerApi;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsPowerManagement"/> class.
    /// </summary>
    /// <param name="loggerFactory">Logger Factory.</param>
    /// <param name="plugin">Plugin.</param>
    /// <exception cref="Win32Exception">If any of the invoked Windows Power API calls fail.</exception>
    public WindowsPowerManagement(ILoggerFactory loggerFactory, Plugin plugin)
    {
        _logger = loggerFactory.CreateLogger<WindowsPowerManagement>();
        // Likely required because upstream libraries don't have `nullable` enabled.
        ArgumentNullException.ThrowIfNull(plugin);
        _plugin = plugin;
        _windowsPowerApi = new WindowsPowerApi(loggerFactory);
        _powerRequest = _windowsPowerApi.PowerCreateRequest();
        _supportsModernStandby = SupportsModernStandby();
        _logger.LogDebug("Modern Standby support: {SupportsModernStandby}", _supportsModernStandby);

        if (_supportsModernStandby)
        {
            // Restore in case the previous shutdown was not clean
            DeactivateAndDeleteModifiedPowerSchemes(false);
        }
    }

    private void CreateAndActivateModifiedPowerScheme()
    {
        Guid activePowerScheme = _windowsPowerApi.PowerGetActiveScheme();
        SavePreviousPowerScheme(activePowerScheme);

        Guid newPowerScheme = _windowsPowerApi.PowerDuplicateScheme(activePowerScheme);

        _windowsPowerApi.PowerWriteFriendlyName(newPowerScheme, JellyfinPowerSchemeName);
        _windowsPowerApi.PowerWriteDescription(newPowerScheme, JellyfinPowerSchemeDescription);

        // Disable power request timeouts: uint.MaxValue (=-1) corresponds to no timeout at all
        _windowsPowerApi.WriteCurrentACPowerRequestTimeout(newPowerScheme, uint.MaxValue);
        _windowsPowerApi.WriteCurrentDCPowerRequestTimeout(newPowerScheme, uint.MaxValue);

        _windowsPowerApi.PowerSetActiveScheme(newPowerScheme);
    }

    private void DeactivateAndDeleteModifiedPowerSchemes(bool expectHavingToRestore)
    {
        Guid activePowerScheme = _windowsPowerApi.PowerGetActiveScheme();
        string activePowerSchemeName = _windowsPowerApi.PowerReadFriendlyName(activePowerScheme);

        if (activePowerSchemeName == JellyfinPowerSchemeName)
        {
            if (!expectHavingToRestore)
            {
                _logger.LogWarning("The modified Jellyfin power scheme is still active, likely due to a previous Jellyfin crash. Restoring the power scheme now.");
            }

            SwitchToNonJellyfinPowerScheme();
        }

        DeleteAllJellyfinPowerSchemes();
    }

    private void SwitchToNonJellyfinPowerScheme()
    {
        if (_plugin.Configuration.PreviousPowerScheme is Guid previousPowerScheme && !IsJellyfinPowerScheme(previousPowerScheme))
        {
            try
            {
                _windowsPowerApi.PowerSetActiveScheme(previousPowerScheme);
                SavePreviousPowerScheme(null);
                return;
            }
            catch (Win32Exception e)
            {
                _logger.LogError(e, "Failed to restore stored power scheme. Trying to restore a system default scheme.");
                SavePreviousPowerScheme(null);
            }
        }
        else
        {
            _logger.LogError("Trying to restore a power scheme, but there is no known valid previous scheme. Trying to restore a system default scheme.");
        }

        // Enable the "Balanced" scheme, which should exist on all Windows machines.
        // If this fails, it is okay to throw here.
        _windowsPowerApi.PowerSetActiveScheme(PInvoke.GUID_TYPICAL_POWER_SAVINGS);
    }

    private void DeleteAllJellyfinPowerSchemes()
    {
        List<Guid> powerSchemes = _windowsPowerApi.PowerEnumerate();
        foreach (Guid scheme in powerSchemes)
        {
            // Delete all Jellyfin power schemes in case there are multiple ones
            if (IsJellyfinPowerScheme(scheme))
            {
                _windowsPowerApi.PowerDeleteScheme(scheme);
            }
        }
    }

    private bool SupportsModernStandby()
    {
        return _windowsPowerApi.GetPwrCapabilities().AoAc;
    }

    private bool IsJellyfinPowerScheme(Guid scheme)
    {
        return _windowsPowerApi.PowerReadFriendlyName(scheme) == JellyfinPowerSchemeName;
    }

    private void SavePreviousPowerScheme(Guid? scheme)
    {
        _plugin.Configuration.PreviousPowerScheme = scheme;
        _plugin.SaveConfiguration();
    }

    /// <summary>
    /// Note: Can show unexpected behavior if called when sleep is already blocked.
    /// </summary>
    /// <exception cref="Win32Exception">If any API calls fail.</exception>
    public void BlockSleep()
    {
        if (_supportsModernStandby)
        {
            CreateAndActivateModifiedPowerScheme();
        }

        _windowsPowerApi.PowerSetSystemRequiredRequest(_powerRequest);
    }

    /// <summary>
    /// Note: Can show unexpected behavior or throw exceptions if called when sleep is not blocked.
    /// </summary>
    /// <exception cref="Win32Exception">If any API calls fail.</exception>
    public void UnblockSleep()
    {
        if (_supportsModernStandby)
        {
            DeactivateAndDeleteModifiedPowerSchemes(true);
        }

        _windowsPowerApi.PowerClearSystemRequiredRequest(_powerRequest);
    }
}
