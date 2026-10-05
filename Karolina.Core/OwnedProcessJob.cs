using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Karolina.Core;

/// <summary>外部工具及其后代归属同一 Windows 作业；退出、停止或超时时统一释放。</summary>
internal sealed class OwnedProcessJob : IDisposable
{
    private SafeFileHandle? handle;
    public OwnedProcessJob()
    {
        handle=CreateJobObject(IntPtr.Zero,null);
        if(handle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error(),"无法创建工具进程作业");
        var limits=new ExtendedLimits { Basic=new BasicLimits { Flags=0x2000 } }; // KILL_ON_JOB_CLOSE
        if(!SetInformationJobObject(handle,9,ref limits,(uint)Marshal.SizeOf<ExtendedLimits>()))
        {int error=Marshal.GetLastWin32Error();Dispose();throw new Win32Exception(error,"无法设置工具进程作业");}
    }
    public void Assign(IntPtr process)
    {
        if(!AssignProcessToJobObject(handle!,process))throw new Win32Exception(Marshal.GetLastWin32Error(),"无法接管外部工具及其后代，已停止本次启动");
    }
    public void Dispose()=>Interlocked.Exchange(ref handle,null)?.Dispose();
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    {
        public long ProcessTime,JobTime;public uint Flags;public UIntPtr MinWorkingSet,MaxWorkingSet;
        public uint ActiveProcesses;public UIntPtr Affinity;public uint Priority,Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits Basic;public IoCounters Io;public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory; }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle job,int informationClass,ref ExtendedLimits limits,uint length);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeFileHandle job,IntPtr process);
}
