using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Venti.LocalDialogue;

internal static class ModelProcessJob
{
    private const int ExtendedLimitInformationClass = 9;
    private const uint KillOnJobClose = 0x2000;

    internal static SafeFileHandle Create()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("本地对话一键预览需要 Windows。");
        }

        SafeFileHandle job = CreateJobObject(0, null);
        if (job.IsInvalid)
        {
            int code = Marshal.GetLastPInvokeError();
            job.Dispose();
            throw new IOException("无法创建本地模型进程的生命周期控制。", new Win32Exception(code));
        }

        var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = KillOnJobClose } };
        if (!SetInformationJobObject(job, ExtendedLimitInformationClass, ref limits, Marshal.SizeOf<ExtendedLimits>()))
        {
            int code = Marshal.GetLastPInvokeError();
            job.Dispose();
            throw new IOException("无法设置本地模型进程的关闭规则。", new Win32Exception(code));
        }
        return job;
    }

    internal static void Assign(SafeFileHandle job, SafeProcessHandle process)
    {
        if (!AssignProcessToJobObject(job, process))
        {
            throw new IOException("无法将本地模型进程绑定到对话窗口。", new Win32Exception(Marshal.GetLastPInvokeError()));
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(nint attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref ExtendedLimits limits, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        internal long PerProcessTime;
        internal long PerJobTime;
        internal uint Flags;
        internal nuint MinWorkingSet;
        internal nuint MaxWorkingSet;
        internal uint ActiveProcesses;
        internal nuint Affinity;
        internal uint PriorityClass;
        internal uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        internal ulong Reads;
        internal ulong Writes;
        internal ulong OtherOperations;
        internal ulong BytesRead;
        internal ulong BytesWritten;
        internal ulong OtherBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        internal BasicLimits Basic;
        internal IoCounters Io;
        internal nuint ProcessMemory;
        internal nuint JobMemory;
        internal nuint PeakProcessMemory;
        internal nuint PeakJobMemory;
    }
}
