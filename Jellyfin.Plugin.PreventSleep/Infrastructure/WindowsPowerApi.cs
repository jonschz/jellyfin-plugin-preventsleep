using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.System.Threading;

namespace Jellyfin.Plugin.PreventSleep.Infrastructure;

internal class WindowsPowerApi(ILoggerFactory loggerFactory)
{
    private readonly ILogger<WindowsPowerApi> _logger = loggerFactory.CreateLogger<WindowsPowerApi>();

    public void PowerSetSystemRequiredRequest(SafeHandle powerRequest)
    {
        if (PInvoke.PowerSetRequest(powerRequest, POWER_REQUEST_TYPE.PowerRequestSystemRequired))
        {
            _logger.LogDebug("PowerSetRequest succeeded: sleep blocked");
        }
        else
        {
            throw NewWin32Exception(nameof(PInvoke.PowerSetRequest));
        }
    }

    public void PowerClearSystemRequiredRequest(SafeHandle powerRequest)
    {
        if (PInvoke.PowerClearRequest(powerRequest, POWER_REQUEST_TYPE.PowerRequestSystemRequired))
        {
            _logger.LogDebug("PowerClearRequest succeeded: sleep re-enabled");
        }
        else
        {
            throw NewWin32Exception(nameof(PInvoke.PowerClearRequest));
        }
    }

    public SafeFileHandle PowerCreateRequest()
    {
        SafeFileHandle powerRequest;
        unsafe
        {
            fixed (char* reason = "Jellyfin is serving files/waiting for the configured amount of time for further requests (blocked by Plugin.PreventSleep)")
            {
                var reasonContext = new REASON_CONTEXT
                {
                    Version = PInvoke.POWER_REQUEST_CONTEXT_VERSION,
                    Flags = POWER_REQUEST_CONTEXT_FLAGS.POWER_REQUEST_CONTEXT_SIMPLE_STRING,
                    Reason =
                    {
                        SimpleReasonString = new PWSTR(reason)
                    }
                };
                powerRequest = PInvoke.PowerCreateRequest(reasonContext);
            }
        }

        if (powerRequest.IsInvalid)
        {
            throw NewWin32Exception(nameof(PInvoke.PowerCreateRequest));
        }

        return powerRequest;
    }

    public Guid PowerGetActiveScheme()
    {
        unsafe
        {
            Guid* activePowerSchemePointer = null;
            WithErrorHandling(
                nameof(PInvoke.PowerGetActiveScheme),
                () => PInvoke.PowerGetActiveScheme(
                    null,
                    out activePowerSchemePointer));
            // If we get here, `PowerGetActiveScheme` has succeeded, thus we need to free the pointer.
            Guid result = *activePowerSchemePointer;
            if (PInvoke.LocalFree((HLOCAL)activePowerSchemePointer) != HLOCAL.Null)
            {
                _logger.LogError("Failed to free memory for GUID: {Code}", Marshal.GetLastPInvokeError());
            }

            return result;
        }
    }

    public void PowerSetActiveScheme(Guid activePowerScheme)
    {
        WithErrorHandling(
            nameof(PInvoke.PowerSetActiveScheme),
            () => PInvoke.PowerSetActiveScheme(
                null,
                activePowerScheme));
        _logger.LogDebug("Activated modified power scheme");
    }

    /// <summary>
    /// Needs to be <c>internal</c> due to type accessibility constraints.
    /// </summary>
    /// <returns><see cref="SYSTEM_POWER_CAPABILITIES"/>.</returns>
    internal SYSTEM_POWER_CAPABILITIES GetPwrCapabilities()
    {
        if (PInvoke.GetPwrCapabilities(out var capabilities))
        {
            return capabilities;
        }
        else
        {
            throw NewWin32Exception(nameof(PInvoke.GetPwrCapabilities));
        }
    }

    public uint ReadCurrentACPowerRequestTimeout(Guid activePowerScheme)
    {
        uint acValueIndex = 0;
        WithErrorHandling(
            nameof(PInvoke.PowerReadACValueIndex),
            () => PInvoke.PowerReadACValueIndex(
                null,
                activePowerScheme,
                PInvoke.GUID_IDLE_RESILIENCY_SUBGROUP,
                PInvoke.GUID_EXECUTION_REQUIRED_REQUEST_TIMEOUT,
                out acValueIndex));
        return acValueIndex;
    }

    public uint ReadCurrentDCPowerRequestTimeout(Guid activePowerScheme)
    {
        uint dcValueIndex = 0;
        WithErrorHandling(
            nameof(PInvoke.PowerReadDCValueIndex),
            // Due to an oversight by Microsoft, PowerReadDCValueIndex returns a `uint` instead of a `WIN32_ERROR`, hence we need to typecast
            () => (WIN32_ERROR)PInvoke.PowerReadDCValueIndex(
                null,
                activePowerScheme,
                PInvoke.GUID_IDLE_RESILIENCY_SUBGROUP,
                PInvoke.GUID_EXECUTION_REQUIRED_REQUEST_TIMEOUT,
                out dcValueIndex));
        return dcValueIndex;
    }

    public void WriteCurrentACPowerRequestTimeout(Guid activePowerScheme, uint acTimeout)
    {
        WithErrorHandling(
            nameof(PInvoke.PowerWriteACValueIndex),
            () => PInvoke.PowerWriteACValueIndex(
                null,
                activePowerScheme,
                PInvoke.GUID_IDLE_RESILIENCY_SUBGROUP,
                PInvoke.GUID_EXECUTION_REQUIRED_REQUEST_TIMEOUT,
                acTimeout));
        _logger.LogDebug("Set AC power request timeout to {Value}", acTimeout);
    }

    public void WriteCurrentDCPowerRequestTimeout(Guid activePowerScheme, uint dcTimeout)
    {
        WithErrorHandling(
            nameof(PInvoke.PowerWriteDCValueIndex),
            // Due to an oversight by Microsoft, PowerReadDCValueIndex returns a `uint` instead of a `WIN32_ERROR`, hence we need to typecast
            () => (WIN32_ERROR)PInvoke.PowerWriteDCValueIndex(
                null,
                activePowerScheme,
                PInvoke.GUID_IDLE_RESILIENCY_SUBGROUP,
                PInvoke.GUID_EXECUTION_REQUIRED_REQUEST_TIMEOUT,
                dcTimeout));
        _logger.LogDebug("Set DC power request timeout to {Value}", dcTimeout);
    }

    /// <summary>
    /// To be used after calling a function via PInvoke that can fail but does not return a WIN32_ERROR.
    /// </summary>
    private static Win32Exception NewWin32Exception(string method)
    {
        var nativeErrorCode = Marshal.GetLastPInvokeError();
        var message = $"{method} failed: {Marshal.GetPInvokeErrorMessage(nativeErrorCode)} ({nativeErrorCode})";
        return new Win32Exception(nativeErrorCode, message);
    }

    /// <summary>
    /// To be used when `fn` returns a WIN32_ERROR.
    /// </summary>
    /// <exception cref="Win32Exception">If the returned value is not WIN32_ERROR.NO_ERROR.</exception>
    private static void WithErrorHandling(string functionName, Func<WIN32_ERROR> fn)
    {
        WIN32_ERROR result = fn();
        if (result != WIN32_ERROR.NO_ERROR)
        {
            throw new Win32Exception((int)result, $"{functionName} failed with {result}");
        }
    }
}
