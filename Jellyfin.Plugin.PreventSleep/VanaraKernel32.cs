/*
    MIT License

    Copyright (c) 2017 David Hall

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
*/

// https://github.com/dahall/Vanara/commit/153533f7e07bb78119dee90520ed5af6b5b584ae

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ConstrainedExecution;
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

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PowerClearRequest(SafePowerRequestObject PowerRequest, POWER_REQUEST_TYPE RequestType);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    public static extern SafePowerRequestObject PowerCreateRequest([In] REASON_CONTEXT Context);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PowerSetRequest(SafePowerRequestObject PowerRequest, POWER_REQUEST_TYPE RequestType);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    public class SafePowerRequestObject : SafeKernelHandle
    {
        public SafePowerRequestObject(IntPtr preexistingHandle, bool ownsHandle = true) : base(preexistingHandle, ownsHandle) { }

        private SafePowerRequestObject() : base() { }
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public class REASON_CONTEXT : IDisposable
    {
        public DIAGNOSTIC_REASON_VERSION Version;
        public DIAGNOSTIC_REASON Flags;
        private DETAIL _reason;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DETAIL
        {
            public IntPtr LocalizedReasonModule;
            public uint LocalizedReasonId;
            public uint ReasonStringCount;
            public IntPtr ReasonStrings;
        }

        public REASON_CONTEXT(string reason)
        {
            Version = DIAGNOSTIC_REASON_VERSION.DIAGNOSTIC_REASON_VERSION;
            Flags = DIAGNOSTIC_REASON.DIAGNOSTIC_REASON_SIMPLE_STRING;
            _reason.LocalizedReasonModule = Marshal.StringToHGlobalUni(reason);
        }

        void IDisposable.Dispose()
        {
            if (Flags == DIAGNOSTIC_REASON.DIAGNOSTIC_REASON_SIMPLE_STRING)
                Marshal.FreeHGlobal(_reason.LocalizedReasonModule);
        }
    }

    public abstract class SafeKernelHandle : SafeHANDLE, IKernelHandle
    {
        protected SafeKernelHandle() : base() { }

        protected SafeKernelHandle(IntPtr preexistingHandle, bool ownsHandle = true) : base(preexistingHandle, ownsHandle) { }

        protected override bool InternalReleaseHandle() => CloseHandle(handle);
    }
}

public interface IHandle
{
    IntPtr DangerousGetHandle();
}

public interface IKernelHandle : IHandle { }

[StructLayout(LayoutKind.Sequential), DebuggerDisplay("{handle}")]
public struct HANDLE : IHandle
{
    private readonly IntPtr handle;

    public HANDLE(IntPtr preexistingHandle) => handle = preexistingHandle;

    public static HANDLE NULL => new(IntPtr.Zero);

    public bool IsNull => handle == IntPtr.Zero;

    public static explicit operator IntPtr(HANDLE h) => h.handle;

    public static implicit operator HANDLE(IntPtr h) => new(h);

    public static implicit operator HANDLE(SafeHandle h) => new(h.DangerousGetHandle());

    public static bool operator !(HANDLE hMem) => hMem.IsNull;

    public static bool operator !=(HANDLE h1, HANDLE h2) => !(h1 == h2);

    public static bool operator ==(HANDLE h1, HANDLE h2) => h1.Equals(h2);

    public override bool Equals(object obj) => obj is HANDLE h && handle == h.handle;

    public override int GetHashCode() => handle.GetHashCode();

    public IntPtr DangerousGetHandle() => handle;
}

[DebuggerDisplay("{handle}")]
public abstract class SafeHANDLE : SafeHandleZeroOrMinusOneIsInvalid, IEquatable<SafeHANDLE>, IHandle
{
    public SafeHANDLE() : base(true)
    {
    }

    protected SafeHANDLE(IntPtr preexistingHandle, bool ownsHandle = true) : base(ownsHandle) => SetHandle(preexistingHandle);

    public bool IsNull => handle == IntPtr.Zero;

    public static bool operator !(SafeHANDLE hMem) => hMem.IsInvalid;

    public static bool operator !=(SafeHANDLE h1, IHandle h2) => !(h1 == h2);

    public static bool operator !=(SafeHANDLE h1, IntPtr h2) => !(h1 == h2);

    public static bool operator ==(SafeHANDLE h1, IHandle h2) => h1?.Equals(h2) ?? h2 is null;

    public static bool operator ==(SafeHANDLE h1, IntPtr h2) => h1?.Equals(h2) ?? false;

    public static implicit operator HANDLE(SafeHANDLE h) => h.handle;

    public bool Equals(SafeHANDLE other) => ReferenceEquals(this, other) || other is not null && handle == other.handle && IsClosed == other.IsClosed;

    public override bool Equals(object obj) => obj switch
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
        if (IsInvalid) return true;
        if (!InternalReleaseHandle()) return false;
        handle = IntPtr.Zero;
        return true;
    }

    protected static T ThrowIfDisposed<T>(T h) where T : SafeHANDLE => h is null || h.IsInvalid ? throw new ObjectDisposedException(typeof(T).Name) : h;
}
