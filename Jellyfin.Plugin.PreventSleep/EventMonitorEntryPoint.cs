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
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
// using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
// using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PreventSleep;

// based on https://stackoverflow.com/a/60512156
[Flags]
public enum EXECUTION_STATE : uint
{
    ES_AWAYMODE_REQUIRED = 0x00000040,
    ES_CONTINUOUS = 0x80000000,
    ES_DISPLAY_REQUIRED = 0x00000002,
    ES_SYSTEM_REQUIRED = 0x00000001
    // ES_USER_PRESENT = 0x00000004
}

public class EventMonitorEntryPoint : IServerEntryPoint
{
    private readonly ISessionManager _sessionManager;
    // these work when reenabled, but are not needed
    // private readonly IServerConfigurationManager _config;
    private readonly ILogger<EventMonitorEntryPoint> _logger;
    // private readonly ILoggerFactory _loggerFactory;
    // private readonly IFileSystem _fileSystem;

    private Timer? _busyTimer;
    private bool _isBusy;

    private bool _disposed;

    public EventMonitorEntryPoint(
        ISessionManager sessionManager,
        // IServerConfigurationManager config,
        ILoggerFactory loggerFactory)
        // , IFileSystem fileSystem)
    {
        // _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<EventMonitorEntryPoint>();
        _sessionManager = sessionManager;
        // _config = config;
        // _fileSystem = fileSystem;
    }

    public Task RunAsync()
    {
        // _logger.LogInformation("Plugin: entry point");

        _sessionManager.PlaybackStart += SessionManager_PlaybackStart;
        _sessionManager.PlaybackStopped += SessionManager_PlaybackStop;
        _sessionManager.PlaybackProgress += SessionManager_PlaybackProgress;

        return Task.CompletedTask;
    }

    private void SessionManager_PlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        // _logger.LogInformation("Plugin: Playback progress");
        /*
        Must check the timer here because PlaybackStart is not necessarily triggered for every stream.
        Example: Server is rebooted while a stream is running and the stream reconnects.
        Then the new instance of the server never triggers a PlaybackStart event.
        */
        StartBusyTimer();
    }

    private void SessionManager_PlaybackStop(object? sender, PlaybackStopEventArgs e)
    {
        // _logger.LogInformation("Plugin: Playback stop");
    }

    private void SessionManager_PlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        // _logger.LogInformation("Plugin: Playback start");
        StartBusyTimer();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern uint SetThreadExecutionState(EXECUTION_STATE esFlags);

    private void UpdateBusyState(object? state)
    {
        var newIsBusy = _sessionManager.Sessions.Any(i => i.NowPlayingItem is not null);

        if (_isBusy != newIsBusy)
        {
            _isBusy = newIsBusy;
            _logger.LogDebug("New busy state: {State}", _isBusy);
        }

        if (_isBusy)
        {
            /* Repeat regular calls to SetThreadExecutionState.
            We can't use ES_CONTINUOUS because the calls may be made from separate multiprocessing instances,
            so there is no guarantee that the flag will be cleared in the correct process. */
            uint oldState = SetThreadExecutionState(EXECUTION_STATE.ES_SYSTEM_REQUIRED);
            _logger.LogDebug("Calling SetThreadExecutionState");
            if (oldState == 0)
            {
                _logger.LogError("Call to SetThreadExecutionState failed");
            }
        }
        else
        {
            // TODO test stopping the timer
            // DisposeBusyTimer();
        }
    }

    private void DisposeBusyTimer()
    {
        _busyTimer?.Dispose();
        _busyTimer = null;
    }

    private void StartBusyTimer()
    {
        // The minimum time to sleep in Windows is 1 minute, so updating the busy state every 30 seconds is on the safe side
        _busyTimer ??= new Timer(UpdateBusyState, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
        _logger.LogDebug("Busy timer started");
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            DisposeBusyTimer();
        }

        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
