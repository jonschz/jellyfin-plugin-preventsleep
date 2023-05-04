/*
Copyright(C) 2018

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
using System.Runtime.InteropServices;
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
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<EventMonitorEntryPoint> _logger;
    private readonly object _powerRequestLock;
    private readonly HybridDictionary _removedDevices;
    private SafePowerRequestObject? _powerRequest;
    private Timer _delayedUnblockTimer;
    private int _unblockSleepDelay;
    private int _blockingSleepBacking;
    private bool _disposed;

    private bool BlockingSleep
    {
        get => Interlocked.Add(ref _blockingSleepBacking, 0) == 1;
        set => Interlocked.Exchange(ref _blockingSleepBacking, value ? 1 : 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if DEBUG
#pragma warning disable CA2254
    private void __FUNCTION__([CallerMemberName] string memberName = "") => _logger.LogInformation(memberName);
#pragma warning restore CA2254
#else
    private static void __FUNCTION__() {}
#endif

    public EventMonitorEntryPoint(
        ISessionManager sessionManager,
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<EventMonitorEntryPoint>();
        _sessionManager = sessionManager;
        _powerRequestLock = new object();
        _removedDevices = new HybridDictionary(5);
        _delayedUnblockTimer = new Timer(UnblockSleep, null, Timeout.Infinite, Timeout.Infinite);
        using var reasonContext = new REASON_CONTEXT("Jellyfin is serving files / waiting for the configured amount of time for further requests (blocked by Plugin.PreventSleep)");
        _powerRequest = PowerCreateRequest(reasonContext);
    }

    public Task RunAsync()
    {
        __FUNCTION__();

        ApplySettingsFromConfig();
        _sessionManager.PlaybackProgress += SessionManager_PlaybackProgress;
        _sessionManager.PlaybackStart += SessionManager_PlaybackStart;
        _sessionManager.PlaybackStopped += SessionManager_PlaybackStop;
        Plugin.Instance!.ConfigurationChanged += Plugin_ConfigurationChanged;

        return Task.CompletedTask;
    }

    private void SessionManager_PlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        __FUNCTION__();

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
#if DEBUG
        _logger.LogInformation("Removed {DeviceId} from list of removed devices", deviceId);
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void SessionManager_PlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
#if DEBUG
        __FUNCTION__();
#endif

        if (_removedDevices.Contains(e.DeviceId))
        {
#if DEBUG
            _logger.LogInformation("PlaybackProgress erroneously called for stopped device {DeviceName}", e.DeviceName);
#endif
            return;
        }

        if ((DateTime.UtcNow - e.Session.LastPlaybackCheckIn).TotalSeconds > 30)
        {
#if DEBUG
            _logger.LogInformation("LastPlaybackCheckIn of {DeviceName} > 30 seconds, not increasing unblock timer's delay", e.DeviceName);
#endif
            return;
        }

        if (!BlockingSleep)
        {
#if DEBUG
            _logger.LogInformation("Attempting to block sleep");
#endif
            lock (_powerRequestLock)
            {
                if (_powerRequest is null)
                {
                    return;
                }

                if (!(BlockingSleep = PowerSetRequest(_powerRequest, POWER_REQUEST_TYPE.PowerRequestSystemRequired)))
                {
                    var errno = Marshal.GetLastWin32Error();
                    var formattedErrMsg = new Win32Exception(errno).Message;
                    _logger.LogWarning("PowerSetRequest failed: {Win32ErrorMessage} ({Win32ErrorCode})", formattedErrMsg, errno);
                    return;
                }

                _logger.LogInformation("PowerSetRequest succeeded: sleep blocked");
            }
        }

        if (_delayedUnblockTimer.Change(_unblockSleepDelay, Timeout.Infinite))
        {
            return;
        }

        _delayedUnblockTimer.Dispose();
        _delayedUnblockTimer = new Timer(UnblockSleep, null, _unblockSleepDelay, Timeout.Infinite);
    }

    private void SessionManager_PlaybackStop(object? sender, PlaybackStopEventArgs e)
    {
        __FUNCTION__();

        var deviceId = e.DeviceId;
        if (_removedDevices.Contains(deviceId))
        {
            return;
        }

        _removedDevices.Add(deviceId, null);
#if DEBUG
        _logger.LogInformation("Added {DeviceId} to list of removed devices", deviceId);
#endif
    }

    private void Plugin_ConfigurationChanged(object? sender, BasePluginConfiguration e)
    {
        ApplySettingsFromConfig();
    }

    private void ApplySettingsFromConfig()
    {
        _unblockSleepDelay = Math.Clamp(Plugin.Instance!.Configuration.UnblockSleepDelay, 1000, int.MaxValue);
    }

    private void UnblockSleep(object? _)
    {
        __FUNCTION__();

        if (!BlockingSleep)
        {
            return;
        }

        lock (_powerRequestLock)
        {
            if (_powerRequest is null)
            {
                return;
            }

            _logger.LogInformation("Calling PowerClearRequest: unblocking sleep");
            PowerClearRequest(_powerRequest, POWER_REQUEST_TYPE.PowerRequestSystemRequired);
            BlockingSleep = false; // ignore errors
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
            _sessionManager.PlaybackStart -= SessionManager_PlaybackStart;
            _sessionManager.PlaybackStopped -= SessionManager_PlaybackStop;

            _delayedUnblockTimer.Dispose();

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
