using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Karolina.Core;

/// <summary>挂起创建、分配作业、恢复运行。限制继承为三条标准管道，接管失败时不运行工具代码。</summary>
internal sealed class WindowsToolProcess : IDisposable
{
    public Process Process { get; }
    public StreamReader Output { get; }
    public StreamReader Error { get; }
    public StreamWriter Input { get; }
    private WindowsToolProcess(Process process,SafeFileHandle output,SafeFileHandle error,SafeFileHandle input)
    {
        Process=process;
        Output=new(new FileStream(output,FileAccess.Read),Encoding.UTF8);
        Error=new(new FileStream(error,FileAccess.Read),Encoding.UTF8);
        Input=new(new FileStream(input,FileAccess.Write),new UTF8Encoding(false));
    }
    public static WindowsToolProcess Start(ProcessStartInfo start,OwnedProcessJob job)
    {
        var handles=new List<SafeFileHandle>();IntPtr attributes=IntPtr.Zero,handleList=IntPtr.Zero;
        bool initialized=false,created=false,resumed=false;ProcessInformation info=default;WindowsToolProcess? child=null;
        var security=new SecurityAttributes { Length=Marshal.SizeOf<SecurityAttributes>(),Inherit=true };
        (SafeFileHandle Read,SafeFileHandle Write) Pipe(bool parentReads)
        {
            if(!CreatePipe(out var read,out var write,ref security,0))throw new Win32Exception(Marshal.GetLastWin32Error(),"无法创建工具管道");
            handles.Add(read);handles.Add(write);
            if(!SetHandleInformation(parentReads?read:write,1,0))throw new Win32Exception(Marshal.GetLastWin32Error(),"无法隔离工具管道");
            return(read,write);
        }
        try
        {
            var output=Pipe(true);var error=Pipe(true);var input=Pipe(false);
            IntPtr size=IntPtr.Zero;InitializeProcThreadAttributeList(IntPtr.Zero,1,0,ref size);
            attributes=Marshal.AllocHGlobal(size);
            if(!InitializeProcThreadAttributeList(attributes,1,0,ref size))throw new Win32Exception(Marshal.GetLastWin32Error(),"无法初始化工具句柄清单");initialized=true;
            handleList=Marshal.AllocHGlobal(3*IntPtr.Size);
            Marshal.WriteIntPtr(handleList,0,input.Read.DangerousGetHandle());Marshal.WriteIntPtr(handleList,IntPtr.Size,output.Write.DangerousGetHandle());Marshal.WriteIntPtr(handleList,2*IntPtr.Size,error.Write.DangerousGetHandle());
            if(!UpdateProcThreadAttribute(attributes,0,(IntPtr)0x20002,handleList,(IntPtr)(3*IntPtr.Size),IntPtr.Zero,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error(),"无法限制工具继承句柄");
            var startup=new StartupInfoEx { Info=new StartupInfo { Size=Marshal.SizeOf<StartupInfoEx>(),Flags=0x100,Input=input.Read.DangerousGetHandle(),Output=output.Write.DangerousGetHandle(),Error=error.Write.DangerousGetHandle() },Attributes=attributes };
            string executable=ResolveExecutable(start.FileName,start.WorkingDirectory);
            var command=new StringBuilder(string.Join(' ',new[]{executable}.Concat(start.ArgumentList).Select(Quote)));
            if(command.Length>=32767)throw new ArgumentException("工具命令行超过Windows长度限制");
            // CREATE_SUSPENDED | CREATE_NO_WINDOW | EXTENDED_STARTUPINFO_PRESENT
            if(!CreateProcess(executable,command,IntPtr.Zero,IntPtr.Zero,true,0x08080004,IntPtr.Zero,start.WorkingDirectory,ref startup,out info))throw new Win32Exception(Marshal.GetLastWin32Error(),"无法创建外部工具");created=true;
            job.Assign(info.Process);
            child=new WindowsToolProcess(Process.GetProcessById((int)info.ProcessId),output.Read,error.Read,input.Write);
            handles.Remove(output.Read);handles.Remove(error.Read);handles.Remove(input.Write);
            if(ResumeThread(info.Thread)==uint.MaxValue)throw new Win32Exception(Marshal.GetLastWin32Error(),"无法恢复工具进程");resumed=true;
            return child;
        }
        finally
        {
            if(created&&!resumed){TerminateProcess(info.Process,1);child?.Dispose();child?.Process.Dispose();}
            if(created){CloseHandle(info.Thread);CloseHandle(info.Process);}
            foreach(var handle in handles)handle.Dispose();
            if(initialized)DeleteProcThreadAttributeList(attributes);
            if(attributes!=IntPtr.Zero)Marshal.FreeHGlobal(attributes);if(handleList!=IntPtr.Zero)Marshal.FreeHGlobal(handleList);
        }
    }
    private static string ResolveExecutable(string name,string root)
    {
        if(name.Contains('/')||name.Contains('\\'))return Path.GetFullPath(Path.Combine(root,name));
        var candidates=new[]{root,AppContext.BaseDirectory,Environment.GetFolderPath(Environment.SpecialFolder.System),Environment.GetFolderPath(Environment.SpecialFolder.Windows)}.Concat((Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)).Where(p=>!string.IsNullOrWhiteSpace(p));
        foreach(string directory in candidates)
        {string path=Path.Combine(directory.Trim('"'),name);if(File.Exists(path))return Path.GetFullPath(path);if(!Path.HasExtension(name)&&File.Exists(path+".exe"))return Path.GetFullPath(path+".exe");}
        throw new FileNotFoundException("找不到外部工具可执行程序；脚本应注册其解释器和参数",name);
    }
    private static string Quote(string value)
    {
        var output=new StringBuilder("\"");int slashes=0;
        foreach(char c in value){if(c=='\\'){slashes++;continue;}output.Append('\\',c=='"'?slashes*2+1:slashes);output.Append(c);slashes=0;}
        return output.Append('\\',slashes*2).Append('"').ToString();
    }
    public void Dispose(){Input.Dispose();Output.Dispose();Error.Dispose();}
    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length;public IntPtr Descriptor;[MarshalAs(UnmanagedType.Bool)] public bool Inherit; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct StartupInfo { public int Size;public IntPtr Reserved,Desktop,Title;public uint X,Y,Width,Height,XChars,YChars,Fill,Flags;public ushort Show,ReservedSize;public IntPtr ReservedBytes,Input,Output,Error; }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo Info;public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr Process,Thread;public uint ProcessId,ThreadId; }
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CreatePipe(out SafeFileHandle read,out SafeFileHandle write,ref SecurityAttributes security,uint size);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool SetHandleInformation(SafeFileHandle handle,uint mask,uint flags);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool InitializeProcThreadAttributeList(IntPtr list,int count,uint flags,ref IntPtr size);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool UpdateProcThreadAttribute(IntPtr list,uint flags,IntPtr attribute,IntPtr value,IntPtr size,IntPtr previous,IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll",EntryPoint="CreateProcessW",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcess(string application,StringBuilder command,IntPtr processSecurity,IntPtr threadSecurity,[MarshalAs(UnmanagedType.Bool)] bool inherit,uint flags,IntPtr environment,string directory,ref StartupInfoEx startup,out ProcessInformation info);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(IntPtr process,uint exitCode);
    [DllImport("kernel32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
