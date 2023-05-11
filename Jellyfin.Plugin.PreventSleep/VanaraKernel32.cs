/*
    Copyright (c) 2017 David Hall 2023 Faheem Pervez, Jonathan Schulz

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program. If not, see<http://www.gnu.org/licenses/>.


    Originally published under MIT License, re-published with modifications
    under GPLv3.
    https://github.com/dahall/Vanara/commit/153533f7e07bb78119dee90520ed5af6b5b584ae
*/

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Jellyfin.Plugin.PreventSleep;

public static class VanaraPInvokeKernel32
{
    public enum POWER_REQUEST_TYPE
    {
        PowerRequestDisplayRequired,
        PowerRequestSystemRequired,
        PowerRequestAwayModeRequired,
        PowerRequestExecutionRequired,
    }

    public enum DIAGNOSTIC_REASON : uint
    {
        DIAGNOSTIC_REASON_SIMPLE_STRING = 0x00000001,
        DIAGNOSTIC_REASON_DETAILED_STRING = 0x00000002,
        DIAGNOSTIC_REASON_NOT_SPECIFIED = 0x80000000
    }

    public enum DIAGNOSTIC_REASON_VERSION
    {
        DIAGNOSTIC_REASON_VERSION = 0
    }

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerClearRequest(SafePowerRequestObject powerRequest, POWER_REQUEST_TYPE requestType);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern SafePowerRequestObject PowerCreateRequest([In] REASON_CONTEXT context);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerSetRequest(SafePowerRequestObject powerRequest, POWER_REQUEST_TYPE requestType);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    public static void SafePowerClearRequest(SafePowerRequestObject powerRequest, POWER_REQUEST_TYPE requestType)
    {
        if (!PowerClearRequest(powerRequest, requestType))
        {
            throw new Win32Exception();
        }
    }

    public static SafePowerRequestObject SafePowerCreateRequest(REASON_CONTEXT context)
    {
        var handle = PowerCreateRequest(context);
        if (handle.IsInvalid)
        {
            throw new Win32Exception();
        }

        return handle;
    }

    public static void SafePowerSetRequest(SafePowerRequestObject powerRequest, POWER_REQUEST_TYPE requestType)
    {
        if (!PowerSetRequest(powerRequest, requestType))
        {
            throw new Win32Exception();
        }
    }

    public class SafePowerRequestObject : SafeKernelHandle
    {
        public SafePowerRequestObject(IntPtr preexistingHandle, bool ownsHandle = true) : base(preexistingHandle, ownsHandle)
        {
        }

        protected SafePowerRequestObject() : base()
        {
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public class REASON_CONTEXT : IDisposable
    {
        private readonly DIAGNOSTIC_REASON_VERSION _version;
        private readonly DIAGNOSTIC_REASON _flags;
        private DETAIL _reason;

        public REASON_CONTEXT(string reason)
        {
            _version = DIAGNOSTIC_REASON_VERSION.DIAGNOSTIC_REASON_VERSION;
            _flags = DIAGNOSTIC_REASON.DIAGNOSTIC_REASON_SIMPLE_STRING;
            _reason._localizedReasonModule = Marshal.StringToHGlobalUni(reason);
        }

        void IDisposable.Dispose()
        {
            if (_flags == DIAGNOSTIC_REASON.DIAGNOSTIC_REASON_SIMPLE_STRING && _reason._localizedReasonModule != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_reason._localizedReasonModule);
                // explicitly set to zero to prevent double free errors
                _reason._localizedReasonModule = IntPtr.Zero;
            }

            GC.SuppressFinalize(this);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DETAIL
        {
            public IntPtr _localizedReasonModule;
            public uint _localizedReasonId;
            public uint _reasonStringCount;
            public IntPtr _reasonStrings;
        }
    }

    public abstract class SafeKernelHandle : SafeHANDLE, IKernelHandle
    {
        protected SafeKernelHandle() : base()
        {
        }

        protected SafeKernelHandle(IntPtr preexistingHandle, bool ownsHandle = true) : base(preexistingHandle, ownsHandle)
        {
        }

        protected override bool InternalReleaseHandle() => CloseHandle(handle);
    }
}

public interface IHandle
{
    IntPtr DangerousGetHandle();
}

public interface IKernelHandle : IHandle
{
}

[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("{handle}")]
public readonly struct HANDLE : IHandle
{
    private readonly IntPtr _handle;

    public HANDLE(IntPtr preexistingHandle) => _handle = preexistingHandle;

    public static HANDLE NULL => new(IntPtr.Zero);

    public bool IsNull => _handle == IntPtr.Zero;

    public static explicit operator IntPtr(HANDLE h) => h._handle;

    public static implicit operator HANDLE(IntPtr h) => new(h);

    public static implicit operator HANDLE(SafeHandle h) => new(h.DangerousGetHandle());

    public static bool operator !(HANDLE hMem) => hMem.IsNull;

    public static bool operator !=(HANDLE h1, HANDLE h2) => !(h1 == h2);

    public static bool operator ==(HANDLE h1, HANDLE h2) => h1.Equals(h2);

    public override bool Equals(object? obj) => obj is HANDLE h && _handle == h._handle;

    public override int GetHashCode() => _handle.GetHashCode();

    public IntPtr DangerousGetHandle() => _handle;
}

[DebuggerDisplay("{handle}")]
public abstract class SafeHANDLE : SafeHandleZeroOrMinusOneIsInvalid, IEquatable<SafeHANDLE>, IHandle
{
    public SafeHANDLE() : base(true)
    {
    }

    protected SafeHANDLE(IntPtr preexistingHandle, bool ownsHandle = true) : base(ownsHandle) => SetHandle(preexistingHandle);

    public bool IsNull => handle == IntPtr.Zero;

    public static implicit operator HANDLE(SafeHANDLE h) => h.handle;

    public static bool operator !(SafeHANDLE hMem) => hMem.IsInvalid;

    public static bool operator !=(SafeHANDLE h1, IHandle h2) => !(h1 == h2);

    public static bool operator !=(SafeHANDLE h1, IntPtr h2) => !(h1 == h2);

    public static bool operator ==(SafeHANDLE h1, IHandle h2) => h1?.Equals(h2) ?? h2 is null;

    public static bool operator ==(SafeHANDLE h1, IntPtr h2) => h1?.Equals(h2) ?? false;

    public bool Equals(SafeHANDLE? other) =>
        ReferenceEquals(this, other) || (other is not null && handle == other.handle && IsClosed == other.IsClosed);

    public override bool Equals(object? obj) => obj switch
    {
        IHandle ih => handle.Equals(ih.DangerousGetHandle()),
        SafeHandle sh => handle.Equals(sh.DangerousGetHandle()),
        IntPtr p => handle.Equals(p),
        _ => base.Equals(obj),
    };

    public override int GetHashCode() => base.GetHashCode();

    public IntPtr ReleaseOwnership()
    {
        var ret = handle;
        SetHandleAsInvalid();
        return ret;
    }

    protected abstract bool InternalReleaseHandle();

    [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
    protected override bool ReleaseHandle()
    {
        if (IsInvalid)
        {
            return true;
        }

        if (!InternalReleaseHandle())
        {
            return false;
        }

        handle = IntPtr.Zero;
        return true;
    }

    protected static T ThrowIfDisposed<T>(T h)
        where T : SafeHANDLE
        => h is null || h.IsInvalid ? throw new ObjectDisposedException(typeof(T).Name) : h;
}
