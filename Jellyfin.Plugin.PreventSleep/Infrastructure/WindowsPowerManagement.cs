using System;
using System.ComponentModel;
using Jellyfin.Plugin.PreventSleep.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace Jellyfin.Plugin.PreventSleep.Infrastructure;

public sealed class WindowsPowerManagement : IPowerManagement
{
    private readonly ILogger<WindowsPowerManagement> _logger;
    private readonly SafeFileHandle _powerRequest;

    /// <summary>
    /// See https://stackoverflow.com/a/23505373 why special handling regarding Modern Standby is needed.
    /// </summary>
    private readonly bool _supportsModernStandby;
    private readonly uint _acTimeoutToBeRestored;
    private readonly uint _dcTimeoutToBeRestored;
    private readonly Plugin _plugin;
    private readonly WindowsPowerApi _windowsPowerApi;
    private Guid _activePowerScheme;

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

        // TODO: revert
        // _supportsModernStandby = SupportsModernStandby();
        _supportsModernStandby = true;

        _logger.LogDebug("Modern Standby support: {SupportsModernStandby}", _supportsModernStandby);

        if (_supportsModernStandby)
        {
            _activePowerScheme = _windowsPowerApi.PowerGetActiveScheme();

            // If Jellyfin crashed during its last run while sleep was blocked, the code to restore the power request timeouts was not called.
            // In that case, `Read**CurrentPowerRequestTimeout()` will always return the overwritten value of -1. So we prefer to take the value
            // from our stored configuration and only read the current one from Windows if nothing needs to be restored now.
            _acTimeoutToBeRestored = plugin.Configuration.ACTimeoutToBeRestoredAtStartup ?? _windowsPowerApi.ReadCurrentACPowerRequestTimeout(_activePowerScheme);
            _dcTimeoutToBeRestored = plugin.Configuration.DCTimeoutToBeRestoredAtStartup ?? _windowsPowerApi.ReadCurrentDCPowerRequestTimeout(_activePowerScheme);
            _logger.LogDebug("Timeouts to be restored after unblocking: AC: {AcTimeout}; DC: {DcTimeout}", _acTimeoutToBeRestored, _dcTimeoutToBeRestored);

            if (plugin.Configuration.ACTimeoutToBeRestoredAtStartup is not null || plugin.Configuration.DCTimeoutToBeRestoredAtStartup is not null)
            {
                _logger.LogWarning("Power request timeouts were not restored correctly, likely due to a previous Jellyfin crash. Restoring now.");
                SetPowerRequestTimeouts(_acTimeoutToBeRestored, _dcTimeoutToBeRestored);

                // We just restored these values, so they no longer need to be restored at the next startup.
                SaveTimeoutsToBeRestored(null, null);
            }
        }
    }

    /// <summary>
    /// Note: Can show unexpected behavior if called when sleep is already blocked.
    /// </summary>
    /// <exception cref="Win32Exception">If any API calls fail.</exception>
    public void BlockSleep()
    {
        if (_supportsModernStandby)
        {
            // In case the power scheme has changed since the last invocation
            _activePowerScheme = _windowsPowerApi.PowerGetActiveScheme();

            // Save the values that need to be restored to the plugin configuration.
            // That way, they will be restored on the next startup if Jellyfin crashes.
            SaveTimeoutsToBeRestored(_acTimeoutToBeRestored, _dcTimeoutToBeRestored);

            // uint.MaxValue (=-1) corresponds to no timeout at all
            SetPowerRequestTimeouts(uint.MaxValue, uint.MaxValue);
        }

        _windowsPowerApi.PowerSetSystemRequiredRequest(_powerRequest);
    }

    /// <summary>
    /// Note: Can show unexpected behavior or throw exceptions if called when sleep is already blocked.
    /// </summary>
    /// <exception cref="Win32Exception">If any API calls fail.</exception>
    public void UnblockSleep()
    {
        if (_supportsModernStandby)
        {
            SetPowerRequestTimeouts(_acTimeoutToBeRestored, _dcTimeoutToBeRestored);

            // We just restored these values, so they no longer need to be restored at the next startup.
            SaveTimeoutsToBeRestored(null, null);
        }

        _windowsPowerApi.PowerClearSystemRequiredRequest(_powerRequest);
    }

    private bool SupportsModernStandby()
    {
        return _windowsPowerApi.GetPwrCapabilities().AoAc;
    }

    private void SaveTimeoutsToBeRestored(uint? acTimeoutToBeRestored, uint? dcTimeoutToBeRestored)
    {
        _plugin.Configuration.ACTimeoutToBeRestoredAtStartup = acTimeoutToBeRestored;
        _plugin.Configuration.DCTimeoutToBeRestoredAtStartup = dcTimeoutToBeRestored;
        _plugin.SaveConfiguration();
    }

    private void SetPowerRequestTimeouts(uint acTimeout, uint dcTimeout)
    {
        _windowsPowerApi.WriteCurrentACPowerRequestTimeout(_activePowerScheme, acTimeout);
        _windowsPowerApi.WriteCurrentDCPowerRequestTimeout(_activePowerScheme, dcTimeout);
        // This step appears is not always required, but according to StackOverflow and qwerty12, it can be for some systems.
        // Since it does not seem to hurt when it is not needed, we just leave it in.
        _windowsPowerApi.PowerSetActiveScheme(_activePowerScheme);
    }
}
