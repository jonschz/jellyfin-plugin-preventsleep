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
using Microsoft.Extensions.Logging;
using static Jellyfin.Plugin.PreventSleep.VanaraPInvokeKernel32;

namespace Jellyfin.Plugin.PreventSleep;

public class EventMonitorEntryPoint : IServerEntryPoint
{
    private const int DelayedMinutes = 15;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<EventMonitorEntryPoint> _logger;
    private readonly object _powerRequestLock;
    private readonly HybridDictionary _removedDevices;
    private SafePowerRequestObject? _powerRequest;
    private Timer? _delayedUnblockTimer;
    private bool _blockingSleep;
    private bool _disposed;

    public EventMonitorEntryPoint(
        ISessionManager sessionManager,
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<EventMonitorEntryPoint>();
        _sessionManager = sessionManager;
        _powerRequestLock = new object();
        _removedDevices = new HybridDictionary(3);
        try
        {
            using var reasonContext = new REASON_CONTEXT($"Jellyfin is serving files/waiting {DelayedMinutes} minutes for another request (blocked by Plugin.PreventSleep)");
            _powerRequest = SafePowerCreateRequest(reasonContext);
        }
        catch (Win32Exception e)
        {
            _logger.LogError("PowerCreateRequest failed: {Win32ErrorMessage} ({Win32ErrorCode})", e.Message, e.NativeErrorCode);
        }
    }

    public Task RunAsync()
    {
        _sessionManager.PlaybackProgress += SessionManager_PlaybackProgress;
        _sessionManager.PlaybackStart += SessionManager_PlaybackStart;
        _sessionManager.PlaybackStopped += SessionManager_PlaybackStop;

        return Task.CompletedTask;
    }

    private void SessionManager_PlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        if (e.MediaInfo is null)
        {
            return;
        }

        if (e.Users.Count == 0)
        {
            return;
        }

        if (e.Item is not null && e.Item.IsThemeMedia)
        {
            return;
        }

        var deviceId = e.DeviceId;
        if (!_removedDevices.Contains(deviceId))
        {
            return;
        }

        _removedDevices.Remove(deviceId);
        _logger.LogDebug("Removed {DeviceId} from list of removed devices", deviceId);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void SessionManager_PlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        if (_removedDevices.Contains(e.DeviceId))
        {
            _logger.LogDebug("PlaybackProgress erroneously called for stopped device {DeviceName}", e.DeviceName);
            return;
        }

        if ((DateTime.UtcNow - e.Session.LastPlaybackCheckIn).TotalSeconds > 30)
        {
            _logger.LogDebug("LastPlaybackCheckIn of {DeviceName} > 30 seconds, not increasing unblock timer's delay", e.DeviceName);
            return;
        }

        if (!_blockingSleep)
        {
            lock (_powerRequestLock)
            {
                if (_powerRequest is null)
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

        const int Delay = DelayedMinutes * 60000;
        if (_delayedUnblockTimer is not null && _delayedUnblockTimer.Change(Delay, Timeout.Infinite))
        {
            return;
        }

        _delayedUnblockTimer?.Dispose();
        _delayedUnblockTimer = new Timer(UnblockSleep, null, Delay, Timeout.Infinite);
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

    #pragma warning disable SA1313 // Disable a spurious warning, see https://github.com/DotNetAnalyzers/StyleCopAnalyzers/issues/2599
    private void UnblockSleep(object? _)
    #pragma warning restore SA1313
    {
        if (!_blockingSleep)
        {
            return;
        }

        lock (_powerRequestLock)
        {
            if (_powerRequest is null)
            {
                return;
            }

            _logger.LogDebug("Calling PowerClearRequest: unblocking sleep");
            try
            {
                SafePowerClearRequest(_powerRequest, POWER_REQUEST_TYPE.PowerRequestSystemRequired);
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

            _delayedUnblockTimer?.Dispose();
            _delayedUnblockTimer = null;

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
