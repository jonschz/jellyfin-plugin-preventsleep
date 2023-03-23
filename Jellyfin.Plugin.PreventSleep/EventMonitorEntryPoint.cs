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
using System.Threading.Tasks;
// using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
// using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using static Jellyfin.Plugin.PreventSleep.VanaraPInvokeKernel32;

namespace Jellyfin.Plugin.PreventSleep;

public class EventMonitorEntryPoint : IServerEntryPoint
{
    private readonly ISessionManager _sessionManager;
    // these work when reenabled, but are not needed
    // private readonly IServerConfigurationManager _config;
    private readonly ILogger<EventMonitorEntryPoint> _logger;
    // private readonly ILoggerFactory _loggerFactory;
    // private readonly IFileSystem _fileSystem;
    private readonly SafePowerRequestObject _powerRequest;
    private bool _blockingSleep;

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
        using var reasonContext = new REASON_CONTEXT("Jellyfin is serving files (blocked by the PreventSleep plugin)");
        _powerRequest = PowerCreateRequest(reasonContext);
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
        BlockSleep();
    }

    private void SessionManager_PlaybackStop(object? sender, PlaybackStopEventArgs e)
    {
        // _logger.LogInformation("Plugin: Playback stop");
        if (!_sessionManager.Sessions.Any(i => i.NowPlayingItem is not null))
        {
            UnblockSleep();
        }
    }

    private void SessionManager_PlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        // _logger.LogInformation("Plugin: Playback start");
        BlockSleep();
    }

    private void BlockSleep()
    {
        if (_blockingSleep)
        {
            return;
        }

        _blockingSleep = PowerSetRequest(_powerRequest, POWER_REQUEST_TYPE.PowerRequestSystemRequired);
    }

    private void UnblockSleep()
    {
        if (!_blockingSleep)
        {
            return;
        }

        PowerClearRequest(_powerRequest, POWER_REQUEST_TYPE.PowerRequestSystemRequired);
        _blockingSleep = false;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            UnblockSleep();
            _powerRequest.Dispose();
        }

        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
