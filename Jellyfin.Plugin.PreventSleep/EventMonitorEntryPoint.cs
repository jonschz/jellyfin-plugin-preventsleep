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
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PreventSleep.Infrastructure;
using Jellyfin.Plugin.PreventSleep.Interface;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PreventSleep;

public class EventMonitorEntryPoint(ISessionManager sessionManager, ILoggerFactory loggerFactory) : IHostedService
{
    private const int TimerInterval = 20000;

    private readonly ISessionManager _sessionManager = sessionManager;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private readonly ILogger<EventMonitorEntryPoint> _logger = loggerFactory.CreateLogger<EventMonitorEntryPoint>();

    /// <summary>
    /// Locks access to <see cref="_unblockTimer"/> and <see cref="_powerManagement"/>.
    /// <br/><br/>
    /// TODO #22: replace with System.Threading.Lock when ditching .NET 8/Jellyfin 10.9 support.
    /// </summary>
    private readonly object _powerRequestLock = new();

    // _unblockTimer is not null if sleep mode is currently blocked
    private Timer? _unblockTimer;
    private TimeSpan _unblockSleepDelay;
    private DateTime _lastCheckin = DateTime.MinValue;

    /// <summary>
    /// This value is `null` if the initialisation failed for some reason. In that case, this plugin is in a degenerate state.
    /// </summary>
    private IPowerManagement? _powerManagement;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _powerManagement = CreatePowerManagement();
        if (_powerManagement is not null)
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
        if (_powerManagement is null)
        {
            return;
        }

        lock (_powerRequestLock)
        {
            if (_unblockTimer is not null)
            {
                return;
            }

            try
            {
                _powerManagement.BlockSleep();
                _unblockTimer = new Timer(UnblockSleepTimerCallback, null, TimerInterval, TimerInterval);
            }
            catch (Win32Exception e)
            {
                _logger.LogError(e, "Failed to block sleep: {Error}", e);
            }
        }
    }

#pragma warning disable CA1859 // It wants us to use the concrete type, but this will not be possible in case we support multiple platforms.
    private IPowerManagement? CreatePowerManagement()
#pragma warning restore CA1859
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                return new WindowsPowerManagement(_loggerFactory, Plugin.Instance!);
            }
            catch (Win32Exception e)
            {
                _logger.LogError(e, "Failed to set up power management. Preventing sleep will not work: {Error}", e);
                return null;
            }
        }

        _logger.LogError("Platform is not supported: {Platform}", RuntimeInformation.OSDescription);

        return null;
    }

    // Periodically check if the PowerRequest can be cleared.
    // This is more efficient than modifying a one-shot timer on every PlaybackProgress
    // which happens multiple times per second.
    private void UnblockSleepTimerCallback(object? state)
    {
        if (_powerManagement is null)
        {
            return;
        }

        var timeSinceCheckin = DateTime.UtcNow - _lastCheckin;

        _logger.LogDebug("Time since last checkin: {TimeElapsed}", timeSinceCheckin);

        if (timeSinceCheckin < _unblockSleepDelay)
        {
            return;
        }

        lock (_powerRequestLock)
        {
            if (_unblockTimer is null)
            {
                return;
            }

            try
            {
                _powerManagement.UnblockSleep();
            }
            catch (Win32Exception e)
            {
                _logger.LogError(e, "Failed to unblock sleep: {Error}", e);
            }

            _unblockTimer.Dispose();
            _unblockTimer = null;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackProgress -= SessionManager_PlaybackProgress;
        Plugin.Instance!.ConfigurationChanged -= Plugin_ConfigurationChanged;

        lock (_powerRequestLock)
        {
            if (_unblockTimer is not null)
            {
                try
                {
                    // Important because it may have to restore persistent changes to the power scheme.
                    _powerManagement?.UnblockSleep();
                }
                catch (Win32Exception e)
                {
                    _logger.LogError(e, "Failed to unblock sleep during teardown: {Error}", e);
                }
            }

            _powerManagement = null;

            _unblockTimer?.Dispose();
            _unblockTimer = null;
        }

        return Task.CompletedTask;
    }
}
