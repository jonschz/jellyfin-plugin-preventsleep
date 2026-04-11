using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
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

    /// <summary>
    /// The size of a <see cref="Guid"/> in memory (should be 16).
    /// Unfortunately, <c>sizeof(Guid)</c> causes problems.
    /// </summary>
    private readonly int _guidSize = Guid.Empty.ToByteArray().Length;

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

    private void LocalFree(HLOCAL mem)
    {
        if (HLOCAL.Null != PInvoke.LocalFree(mem))
        {
            // We do not throw here since this is not a critical problem, and is very unlikely to happen anyway
            _logger.LogError("Failed to free memory for GUID: {Code}", Marshal.GetLastPInvokeError());
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

        _logger.LogDebug("Created power request");
        return powerRequest;
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

    public void PowerDeleteScheme(Guid scheme)
    {
        WithErrorHandling(
            nameof(PInvoke.PowerDeleteScheme),
            () => PInvoke.PowerDeleteScheme(null, scheme));
        _logger.LogDebug("Deleted power scheme {Scheme}", scheme);
    }

    public Guid PowerDuplicateScheme(Guid scheme)
    {
        unsafe
        {
            Guid* newPowerSchemePointer = null;
            WithErrorHandling(
                nameof(PInvoke.PowerDuplicateScheme),
                () => PInvoke.PowerDuplicateScheme(null, scheme, ref newPowerSchemePointer));
            // If we get here, `PowerDuplicateScheme` has succeeded, thus we need to free the pointer.
            Guid result = *newPowerSchemePointer;
            LocalFree((HLOCAL)newPowerSchemePointer);
            _logger.LogDebug("Duplicated power scheme {OldScheme} to {NewScheme}", scheme, result);
            return result;
        }
    }

    public List<Guid> PowerEnumerate()
    {
        List<Guid> guids = [];
        byte[] buffer = new byte[_guidSize];
        uint size = (uint)_guidSize;
        bool done = false;

        for (uint i = 0; !done; i++)
        {
            WithErrorHandling(
                nameof(PInvoke.PowerEnumerate),
                () =>
                {
                    WIN32_ERROR result = PInvoke.PowerEnumerate(null, null, null, POWER_DATA_ACCESSOR.ACCESS_SCHEME, i, buffer, ref size);
                    if (result == WIN32_ERROR.ERROR_NO_MORE_ITEMS)
                    {
                        done = true;
                        return WIN32_ERROR.NO_ERROR;
                    }

                    return result;
                });
            if (!done)
            {
                guids.Add(new Guid(buffer));
            }
        }

        return guids;
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
            LocalFree((HLOCAL)activePowerSchemePointer);

            return result;
        }
    }

    public string PowerReadFriendlyName(Guid scheme)
    {
        uint bufferSize = 0;
        // No wrapper since this is expected to error with `WIN32_ERROR.ERROR_MORE_DATA`.
        PInvoke.PowerReadFriendlyName(null, scheme, null, null, null, ref bufferSize);

        byte[] buffer = new byte[bufferSize];

        WithErrorHandling(
            nameof(PInvoke.PowerReadFriendlyName),
            () => PInvoke.PowerReadFriendlyName(null, scheme, null, null, buffer, ref bufferSize));
        string result = Encoding.Unicode.GetString(buffer);
        return result.TrimEnd('\0'); // There is always at least one null terminator, but there can be multiple
    }

    public void PowerSetActiveScheme(Guid scheme)
    {
        WithErrorHandling(
            nameof(PInvoke.PowerSetActiveScheme),
            () => PInvoke.PowerSetActiveScheme(
                null,
                scheme));
        _logger.LogDebug("Activated power scheme {Scheme}", scheme);
    }

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

    public void WriteCurrentACPowerRequestTimeout(Guid scheme, uint acTimeout)
    {
        WithErrorHandling(
            nameof(PInvoke.PowerWriteACValueIndex),
            () => PInvoke.PowerWriteACValueIndex(
                null,
                scheme,
                PInvoke.GUID_IDLE_RESILIENCY_SUBGROUP,
                PInvoke.GUID_EXECUTION_REQUIRED_REQUEST_TIMEOUT,
                acTimeout));
        _logger.LogDebug("Set AC power request timeout for scheme {Scheme} to {Value}", scheme, acTimeout);
    }

    public void WriteCurrentDCPowerRequestTimeout(Guid scheme, uint dcTimeout)
    {
        WithErrorHandling(
            nameof(PInvoke.PowerWriteDCValueIndex),
            // Due to an oversight by Microsoft, PowerReadDCValueIndex returns a `uint` instead of a `WIN32_ERROR`, hence we need to typecast
            () => (WIN32_ERROR)PInvoke.PowerWriteDCValueIndex(
                null,
                scheme,
                PInvoke.GUID_IDLE_RESILIENCY_SUBGROUP,
                PInvoke.GUID_EXECUTION_REQUIRED_REQUEST_TIMEOUT,
                dcTimeout));
        _logger.LogDebug("Set DC power request timeout for scheme {Scheme} to {Value}", scheme, dcTimeout);
    }

    public void PowerWriteDescription(Guid scheme, string description)
    {
        byte[] buffer = Encoding.Unicode.GetBytes(description + "\0");
        WithErrorHandling(
            nameof(PInvoke.PowerWriteDescription),
            () => PInvoke.PowerWriteDescription(null, scheme, null, null, buffer));
        _logger.LogDebug("Changed description of power scheme {Scheme} to {Name}", scheme, description);
    }

    public void PowerWriteFriendlyName(Guid scheme, string name)
    {
        byte[] buffer = Encoding.Unicode.GetBytes(name + "\0");
        WithErrorHandling(
            nameof(PInvoke.PowerWriteFriendlyName),
            () => PInvoke.PowerWriteFriendlyName(null, scheme, null, null, buffer));
        _logger.LogDebug("Changed friendly name of power scheme {Scheme} to {Name}", scheme, name);
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
