/*
Copyright(C) 2018-2026

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program. If not, see<http://www.gnu.org/licenses/>.
*/

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.System.Threading;

namespace Jellyfin.Plugin.PreventSleep;

public class EventMonitorEntryPoint : IHostedService
{
    private const int TimerInterval = 20000;

#pragma warning disable SA1310 // Field names should not contain underscore
    public static readonly Guid GUID_IDLE_RESILIENCY_SUBGROUP = new(0x2e601130, 0x5351, 0x4d9d, 0x8e, 0x4, 0x25, 0x29, 0x66, 0xba, 0xd0, 0x54);
    public static readonly Guid GUID_EXECUTION_REQUIRED_REQUEST_TIMEOUT = new(0x3166bc41, 0x7e98, 0x4e03, 0xb3, 0x4e, 0xec, 0xf, 0x5f, 0x2b, 0x21, 0x8e);
#pragma warning restore SA1310 // Field names should not contain underscore

    private readonly ISessionManager _sessionManager;
    private readonly ILogger<EventMonitorEntryPoint> _logger;
    private readonly object _powerRequestLock; // TODO: replace with System.Threading.Lock when ditching .NET 8/Jellyfin 10.9 support
    private readonly bool _isDebugLogEnabled;
    private readonly bool _hasModernStandby;
    private readonly SafeFileHandle _powerRequest;
    // _unblockTimer is not null if sleep mode is currently blocked
    private Timer? _unblockTimer;
    private TimeSpan _unblockSleepDelay;
    private DateTime _lastCheckin;
    private uint _acTimeoutToBeRestored;
    private uint _dcTimeoutToBeRestored;
    private Guid _activePowerScheme;

    public EventMonitorEntryPoint(
        ISessionManager sessionManager,
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<EventMonitorEntryPoint>();
        _sessionManager = sessionManager;
        _powerRequestLock = new object();
        _lastCheckin = DateTime.MinValue;
        _isDebugLogEnabled = _logger.IsEnabled(LogLevel.Debug);
        var reasonContext = new REASON_CONTEXT
        {
            Version = PInvoke.POWER_REQUEST_CONTEXT_VERSION,
            Flags = POWER_REQUEST_CONTEXT_FLAGS.POWER_REQUEST_CONTEXT_SIMPLE_STRING,
        };
        unsafe
        {
            fixed (char* reason = "Jellyfin is serving files/waiting for the configured amount of time for further requests (blocked by Plugin.PreventSleep)")
            {
                reasonContext.Reason.SimpleReasonString = new PWSTR(reason);
                _powerRequest = PInvoke.PowerCreateRequest(reasonContext);
            }
        }

        if (_powerRequest.IsInvalid)
        {
            LogWin32Error("PowerCreateRequest");
        }

        SYSTEM_POWER_CAPABILITIES capabilities;
        if (PInvoke.GetPwrCapabilities(out capabilities))
        {
            _hasModernStandby = capabilities.AoAc;
            _logger.LogDebug("Modern Standby support: {HasModernStandby}", _hasModernStandby);
        }
        else
        {
            LogWin32Error("GetPwrCapabilities");
            _logger.LogError("Failed to determine whether this system supports Modern Standby. Defaulting to 'no'.");
            _hasModernStandby = false;
        }

        if (_hasModernStandby)
        {
            // Plan for this scheme:
            // - get a new "currently active" one once we start blocking sleep
            // - revert the setting for the stored GUID - it might have changed
            // TODO: Restore saved values here if appropriate

            UpdateActivePowerScheme();

            // TODO: error handling
            PInvoke.PowerReadACValueIndex(null, _activePowerScheme, GUID_IDLE_RESILIENCY_SUBGROUP, GUID_EXECUTION_REQUIRED_REQUEST_TIMEOUT, out var acValueIndex);
            PInvoke.PowerReadDCValueIndex(null, _activePowerScheme, GUID_IDLE_RESILIENCY_SUBGROUP, GUID_EXECUTION_REQUIRED_REQUEST_TIMEOUT, out var dcValueIndex);

            // TODO: save backup to settings

            _acTimeoutToBeRestored = acValueIndex;
            _dcTimeoutToBeRestored = dcValueIndex;
            _logger.LogDebug("Timeout to be restored: AC: {AcTimeout}; DC: {DcTimeout}", _acTimeoutToBeRestored, _dcTimeoutToBeRestored);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_powerRequest is { IsInvalid: false, IsClosed: false })
        {
            ApplySettingsFromConfig();
            _sessionManager.PlaybackProgress += SessionManager_PlaybackProgress;
            Plugin.Instance!.ConfigurationChanged += Plugin_ConfigurationChanged;
        }

        return Task.CompletedTask;
    }

    private void SessionManager_PlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        // This most likely does not require locks / synchronisation; even if some other thread changes _lastCheckin
        // in between the read and the write below, not much can happen
        if (e.Session.LastPlaybackCheckIn > _lastCheckin)
        {
            _lastCheckin = e.Session.LastPlaybackCheckIn;
        }

        if (_unblockTimer is null)
        {
            BlockSleep();
        }
    }

    private void Plugin_ConfigurationChanged(object? sender, BasePluginConfiguration e)
    {
        ApplySettingsFromConfig();
    }

    private void ApplySettingsFromConfig()
    {
        // We add an additional delay of 15 seconds because Session.LastPlaybackCheckIn can lag behind UtcNow
        // by 10 seconds (and possibly more) even if the stream if healthy.
        const double AddedDelay = .25;
        _unblockSleepDelay = TimeSpan.FromMinutes(
            AddedDelay + Math.Clamp(Plugin.Instance!.Configuration.UnblockSleepDelay, 1, int.MaxValue));
    }

    private void BlockSleep()
    {
        lock (_powerRequestLock)
        {
            // check _unblockTimer again for thread safety; we only ever change _unblockTimer
            // when _powerRequestLock has been acquired
            if (_powerRequest.IsClosed || _unblockTimer is not null)
            {
                return;
            }

            if (_hasModernStandby)
            {
                // In case the power scheme has changed since the last invocation
                UpdateActivePowerScheme();
                if (!SetPowerRequestTimeouts(uint.MaxValue, uint.MaxValue))
                {
                    // Error has already been logged
                    return;
                }
            }

            if (PInvoke.PowerSetRequest(_powerRequest, POWER_REQUEST_TYPE.PowerRequestSystemRequired))
            {
                _unblockTimer = new Timer(UnblockSleepTimerCallback, null, TimerInterval, TimerInterval);
                _logger.LogDebug("PowerSetRequest succeeded: sleep blocked");
            }
            else
            {
                LogWin32Error("PowerSetRequest");
            }
        }
    }

    // Periodically check if the PowerRequest can be cleared.
    // This is more efficient than modifying a one-shot timer on every PlaybackProgress
    // which happens multiple times per second.
    private void UnblockSleepTimerCallback(object? state)
    {
        var timeSinceCheckin = DateTime.UtcNow - _lastCheckin;
        if (_isDebugLogEnabled)
        {
            _logger.LogDebug("Time since last checkin: {TimeElapsed}", timeSinceCheckin);
        }

        if (timeSinceCheckin < _unblockSleepDelay)
        {
            return;
        }

        lock (_powerRequestLock)
        {
            if (_powerRequest.IsClosed || _unblockTimer is null)
            {
                return;
            }

            if (_hasModernStandby)
            {
                // Here we don't care if this succeeds, we just try our best to revert the changes we made
                SetPowerRequestTimeouts(_acTimeoutToBeRestored, _dcTimeoutToBeRestored);
            }

            if (PInvoke.PowerClearRequest(_powerRequest, POWER_REQUEST_TYPE.PowerRequestSystemRequired))
            {
                _logger.LogDebug("PowerClearRequest succeeded: sleep re-enabled");
            }
            else
            {
                LogWin32Error("PowerClearRequest");
                return;
            }

            _unblockTimer.Dispose();
            _unblockTimer = null;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackProgress -= SessionManager_PlaybackProgress;
        Plugin.Instance!.ConfigurationChanged -= Plugin_ConfigurationChanged;

        _unblockTimer?.Dispose();
        _unblockTimer = null;

        lock (_powerRequestLock)
        {
            _powerRequest.Dispose();
        }

        return Task.CompletedTask;
    }

    private void UpdateActivePowerScheme()
    {
        unsafe
        {
            PInvoke.PowerGetActiveScheme(null, out var activePowerSchemePointer);
            _activePowerScheme = *activePowerSchemePointer;
        }
    }

    private bool SetPowerRequestTimeouts(uint acTimeout, uint dcTimeout)
    {
        bool succeeded = true;

        WIN32_ERROR error = PInvoke.PowerWriteACValueIndex(null, _activePowerScheme, GUID_IDLE_RESILIENCY_SUBGROUP, GUID_EXECUTION_REQUIRED_REQUEST_TIMEOUT, acTimeout);
        if (error == WIN32_ERROR.NO_ERROR)
        {
            _logger.LogDebug("Set AC power request timeout to {Value}", acTimeout);
        }
        else
        {
            _logger.LogError("Failed to overwrite AC power request timeout: {Error}", error);
            succeeded = false;
        }

        // Almost the same function, but the signature is different in PInvoke, hence the typecast. Not a surprise given how poorly these are documented...
        error = (WIN32_ERROR)PInvoke.PowerWriteDCValueIndex(null, _activePowerScheme, GUID_IDLE_RESILIENCY_SUBGROUP, GUID_EXECUTION_REQUIRED_REQUEST_TIMEOUT, dcTimeout);
        if (error == WIN32_ERROR.NO_ERROR)
        {
            _logger.LogDebug("Set DC power request timeout to {Value}", dcTimeout);
        }
        else
        {
            _logger.LogError("Failed to overwrite DC power request timeout: {Error}", error);
            succeeded = false;
        }

        // TODO: Verify if required (according to Stackoverflow, it is)
        error = PInvoke.PowerSetActiveScheme(null, _activePowerScheme);
        if (error == WIN32_ERROR.NO_ERROR)
        {
            _logger.LogDebug("Activated modified power scheme");
        }
        else
        {
            _logger.LogError("Failed to activate modified power scheme: {Error}", error);
            succeeded = false;
        }

        return succeeded;
    }

    private void LogWin32Error(string method)
    {
        var nativeErrorCode = Marshal.GetLastPInvokeError();
        var message = Marshal.GetPInvokeErrorMessage(nativeErrorCode);
        this._logger.LogError("{Method} failed: {Win32ErrorMessage} ({Win32ErrorCode})", method, message, nativeErrorCode);
    }
}
