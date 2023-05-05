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
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
    private const int TimerInterval = 10000;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<EventMonitorEntryPoint> _logger;
    private readonly object _powerRequestLock;
    private readonly HybridDictionary _removedDevices;
    private readonly Timer _unblockTimer;
    private SafePowerRequestObject? _powerRequest;
    private TimeSpan _unblockSleepDelay;
    private bool _blockingSleep;
    private bool _disposed;
    private DateTime _lastCheckin;

    public EventMonitorEntryPoint(
        ISessionManager sessionManager,
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<EventMonitorEntryPoint>();
        _sessionManager = sessionManager;
        _powerRequestLock = new object();
        _removedDevices = new HybridDictionary(5);
        _lastCheckin = DateTime.MinValue;
        _unblockTimer = new Timer(UnblockSleepTimerCallback, null, TimerInterval, TimerInterval);
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
        ApplySettingsFromConfig();
        _sessionManager.PlaybackProgress += SessionManager_PlaybackProgress;
        _sessionManager.PlaybackStart += SessionManager_PlaybackStart;
        _sessionManager.PlaybackStopped += SessionManager_PlaybackStop;
        Plugin.Instance!.ConfigurationChanged += Plugin_ConfigurationChanged;

        return Task.CompletedTask;
    }

    private void SessionManager_PlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        var deviceId = e.DeviceId;
        if ((e.MediaInfo is not null) &&
            (e.Users.Count > 0) &&
            (e.Item is null || !e.Item.IsThemeMedia) &&
            _removedDevices.Contains(deviceId))
        {
            _removedDevices.Remove(deviceId);
            _logger.LogDebug("Removed {DeviceId} from list of removed devices", deviceId);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void SessionManager_PlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        if (_removedDevices.Contains(e.DeviceId))
        {
            _logger.LogDebug("PlaybackProgress erroneously called for stopped device {DeviceName}", e.DeviceName);
            return;
        }

        // This most likely does not require locks / synchronisation; even if some other thread changes _lastCheckin
        // in between the read and the write below, not much can happen
        if (e.Session.LastPlaybackCheckIn > _lastCheckin)
        {
            _lastCheckin = e.Session.LastPlaybackCheckIn;
        }

        BlockSleep();
    }

    private void SessionManager_PlaybackStop(object? sender, PlaybackStopEventArgs e)
    {
        var deviceId = e.DeviceId;
        if (_removedDevices.Contains(deviceId))
        {
            return;
        }

        _removedDevices.Add(deviceId, null);
        _logger.LogDebug("Added {DeviceId} to list of removed devices", deviceId);
    }

    private void Plugin_ConfigurationChanged(object? sender, BasePluginConfiguration e)
    {
        ApplySettingsFromConfig();
    }

    private void ApplySettingsFromConfig()
    {
        _unblockSleepDelay = TimeSpan.FromMinutes(Math.Clamp(Plugin.Instance!.Configuration.UnblockSleepDelay, 1, int.MaxValue));
    }

    private void BlockSleep()
    {
        if (_blockingSleep)
        {
            return;
        }

        lock (_powerRequestLock)
        {
            // check _blockingSleep again for thread safety
            if (_powerRequest is null || _blockingSleep)
            {
                return;
            }

            try
            {
                // TODO if PowerSetRequest fails, this will be re-attempted multiple times a second.
                // Is there any chance that PowerSetRequest will fail on the first attempt but succeed soon after?
                // If not, I would move "_blockingSleep = True" outside of this try-except block. Then, only a (failing)
                // PowerClearRequest will be called later.
                _logger.LogDebug("Attempting to block sleep");
                SafePowerSetRequest(_powerRequest, POWER_REQUEST_TYPE.PowerRequestSystemRequired);
                _blockingSleep = true;
                _logger.LogDebug("PowerSetRequest succeeded: sleep blocked");
            }
            catch (Win32Exception err)
            {
                _logger.LogError("PowerSetRequest failed: {Win32ErrorMessage} ({Win32ErrorCode})", err.Message, err.NativeErrorCode);
                return;
            }
        }
    }

    // Periodically check if the PowerRequest can be cleared.
    // This is more efficient than modifying a one-shot timer on every PlaybackProgress
    // which happens multiple times per second.
    #pragma warning disable SA1313 // Disable a spurious warning, see https://github.com/DotNetAnalyzers/StyleCopAnalyzers/issues/2599
    private void UnblockSleepTimerCallback(object? _)
    #pragma warning restore SA1313
    {
        _logger.LogDebug("Timer to unblock sleep");
        if (!_blockingSleep)
        {
            return;
        }

        var timeSinceCheckin = DateTime.UtcNow - _lastCheckin;
        if (timeSinceCheckin < _unblockSleepDelay)
        {
            _logger.LogDebug("Time since checkin: {TimeElapsed}", timeSinceCheckin);
            return;
        }

        lock (_powerRequestLock)
        {
            // check _blockingSleep again for thread safety
            if (_powerRequest is null || !_blockingSleep)
            {
                return;
            }

            _logger.LogDebug("Calling PowerClearRequest: unblocking sleep");
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

            _blockingSleep = false; // ignore errors
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
            _sessionManager.PlaybackStart -= SessionManager_PlaybackStart;
            _sessionManager.PlaybackStopped -= SessionManager_PlaybackStop;
            Plugin.Instance!.ConfigurationChanged -= Plugin_ConfigurationChanged;

            _unblockTimer.Dispose();

            lock (_powerRequestLock)
            {
                _powerRequest?.Dispose();
                _powerRequest = null;
            }

            _removedDevices.Clear();
        }

        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
