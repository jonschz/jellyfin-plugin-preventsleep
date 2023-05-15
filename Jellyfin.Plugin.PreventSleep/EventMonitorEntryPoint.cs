/*
Copyright(C) 2018, 2023

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
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Logging;
using static Jellyfin.Plugin.PreventSleep.VanaraPInvokeKernel32;

namespace Jellyfin.Plugin.PreventSleep;

public class EventMonitorEntryPoint : IServerEntryPoint
{
    private const int TimerInterval = 20000;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<EventMonitorEntryPoint> _logger;
    private readonly object _powerRequestLock;
    private readonly bool _isDebugLogEnabled;
    // _unblockTimer is not null iff sleep mode is currently blocked
    private Timer? _unblockTimer;
    private SafePowerRequestObject? _powerRequest;
    private TimeSpan _unblockSleepDelay;
    private bool _disposed;
    private DateTime _lastCheckin;

    public EventMonitorEntryPoint(
        ISessionManager sessionManager,
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<EventMonitorEntryPoint>();
        _sessionManager = sessionManager;
        _powerRequestLock = new object();
        _lastCheckin = DateTime.MinValue;
        _isDebugLogEnabled = _logger.IsEnabled(LogLevel.Debug);
        try
        {
            using var reasonContext = new REASON_CONTEXT("Jellyfin is serving files/waiting for the configured amount of time for further requests (blocked by Plugin.PreventSleep)");
            _powerRequest = SafePowerCreateRequest(reasonContext);
        }
        catch (Win32Exception e)
        {
            _logger.LogError("PowerCreateRequest failed: {Win32ErrorMessage} ({Win32ErrorCode})", e.Message, e.NativeErrorCode);
        }
    }

    public Task RunAsync()
    {
        if (_powerRequest is not null)
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
            if (_powerRequest is null || _unblockTimer is not null)
            {
                return;
            }

            try
            {
                SafePowerSetRequest(_powerRequest, POWER_REQUEST_TYPE.PowerRequestSystemRequired);
                _unblockTimer = new Timer(UnblockSleepTimerCallback, null, TimerInterval, TimerInterval);
                _logger.LogDebug("PowerSetRequest succeeded: sleep blocked");
            }
            catch (Win32Exception err)
            {
                _logger.LogError("PowerSetRequest failed: {Win32ErrorMessage} ({Win32ErrorCode})", err.Message, err.NativeErrorCode);
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
            if (_powerRequest is null || _unblockTimer is null)
            {
                return;
            }

            try
            {
                SafePowerClearRequest(_powerRequest, POWER_REQUEST_TYPE.PowerRequestSystemRequired);
                _logger.LogDebug("PowerClearRequest succeeded: sleep re-enabled");
            }
            catch (Win32Exception e)
            {
                _logger.LogError("PowerClearRequest failed: {Win32ErrorMessage} ({Win32ErrorCode})", e.Message, e.NativeErrorCode);
                return;
            }

            _unblockTimer.Dispose();
            _unblockTimer = null;
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _sessionManager.PlaybackProgress -= SessionManager_PlaybackProgress;
            Plugin.Instance!.ConfigurationChanged -= Plugin_ConfigurationChanged;

            _unblockTimer?.Dispose();
            _unblockTimer = null;

            lock (_powerRequestLock)
            {
                _powerRequest?.Dispose();
                _powerRequest = null;
            }
        }

        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
