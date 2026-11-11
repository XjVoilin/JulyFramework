#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

// Isolated Windows GPU harness. It uses OS DLLs; no SDK, CUDA or install.
// The HLSL kernel is also usable in Unity ComputeShader.
namespace July.Physics.Authoring.PickingProxy.Gpu
{
internal sealed class D3D11Compute : IDisposable
{
    [DllImport("d3d11.dll",ExactSpelling=true),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int D3D11CreateDevice(IntPtr adapter,int driver,IntPtr software,uint flags,int[] levels,uint count,uint sdk,out IntPtr device,out int level,out IntPtr context);
    [DllImport("d3dcompiler_47.dll",ExactSpelling=true,CharSet=CharSet.Ansi),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int D3DCompile(byte[] data,UIntPtr length,string name,IntPtr defines,IntPtr include,string entry,string target,uint flags,uint flags2,out IntPtr code,out IntPtr errors);
    [StructLayout(LayoutKind.Sequential)] private struct BufferDesc {public uint Bytes,Usage,Bind,Cpu,Misc,Stride;}
    [StructLayout(LayoutKind.Sequential)] private struct InitialData {public IntPtr Data;public uint Pitch,Slice;}
    [StructLayout(LayoutKind.Sequential)] private struct Mapped {public IntPtr Data;public uint Pitch,Depth;}
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateBuffer(IntPtr self,ref BufferDesc desc,IntPtr data,out IntPtr buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateView(IntPtr self,IntPtr buffer,IntPtr desc,out IntPtr view);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateShader(IntPtr self,IntPtr code,UIntPtr length,IntPtr linkage,out IntPtr shader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate IntPtr BlobPointer(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate UIntPtr BlobSize(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetViews(IntPtr self,uint start,uint count,IntPtr views);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetOutputs(IntPtr self,uint start,uint count,IntPtr views,IntPtr counts);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetShader(IntPtr self,IntPtr shader,IntPtr instances,uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Dispatch(IntPtr self,uint x,uint y,uint z);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CopyResource(IntPtr self,IntPtr destination,IntPtr source);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Map(IntPtr self,IntPtr resource,uint subresource,uint map,uint flags,out Mapped data);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Unmap(IntPtr self,IntPtr resource,uint subresource);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int QueryInterface(IntPtr self,ref Guid id,out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetAdapter(IntPtr self,out IntPtr adapter);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct AdapterDesc
    { [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Name;public uint Vendor,Device,Subsys,Revision;public UIntPtr DedicatedVideo,DedicatedSystem,SharedSystem;public ulong Luid; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetAdapterDesc(IntPtr self,out AdapterDesc desc);
    private readonly List<IntPtr> _resources=new List<IntPtr>();
    private readonly IntPtr _device,_context;
    internal string AdapterName
    {
        get
        {
            var id=new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");Marshal.ThrowExceptionForHR(Method<QueryInterface>(_device,0)(_device,ref id,out var dxgi));
            try{Marshal.ThrowExceptionForHR(Method<GetAdapter>(dxgi,7)(dxgi,out var adapter));try{Marshal.ThrowExceptionForHR(Method<GetAdapterDesc>(adapter,8)(adapter,out var desc));return desc.Name;}finally{Marshal.Release(adapter);}}finally{Marshal.Release(dxgi);}
        }
    }
    internal D3D11Compute()
    {
        int hr=D3D11CreateDevice(IntPtr.Zero,1,IntPtr.Zero,0,new[]{0xb000},1,7,out _device,out _,out _context);
        Marshal.ThrowExceptionForHR(hr);_resources.Add(_device);_resources.Add(_context);
    }
    private static T Method<T>(IntPtr self,int index) where T:Delegate =>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(self),index*IntPtr.Size));
    private IntPtr Keep(IntPtr value){_resources.Add(value);return value;}
    internal void Release(IntPtr value){if(!_resources.Remove(value))throw new InvalidOperationException("Unknown GPU resource owner.");Marshal.Release(value);}
    internal IntPtr Shader(string hlsl)
    {
        byte[] bytes=Encoding.UTF8.GetBytes(hlsl);int hr=D3DCompile(bytes,(UIntPtr)bytes.Length,"PickingVisibility",IntPtr.Zero,IntPtr.Zero,"CSMain","cs_5_0",32768,0,out var code,out var errors);
        string? message=errors==IntPtr.Zero?null:Marshal.PtrToStringAnsi(Method<BlobPointer>(errors,3)(errors));if(errors!=IntPtr.Zero)Marshal.Release(errors);
        if(hr<0){if(code!=IntPtr.Zero)Marshal.Release(code);throw new InvalidOperationException("Compute shader compilation failed: "+message);}
        try{Marshal.ThrowExceptionForHR(Method<CreateShader>(_device,18)(_device,Method<BlobPointer>(code,3)(code),Method<BlobSize>(code,4)(code),IntPtr.Zero,out var shader));return Keep(shader);}finally{Marshal.Release(code);}
    }
    private IntPtr Buffer(byte[]? data,int bytes,int stride,uint bind,bool staging=false)
    {
        var desc=new BufferDesc{Bytes=(uint)bytes,Usage=staging?3u:0u,Bind=bind,Cpu=staging?0x20000u:0u,Misc=staging?0u:64u,Stride=staging?0u:(uint)stride};IntPtr initial=IntPtr.Zero;GCHandle pin=default;
        try
        {
            if(data!=null){pin=GCHandle.Alloc(data,GCHandleType.Pinned);initial=Marshal.AllocHGlobal(Marshal.SizeOf<InitialData>());Marshal.StructureToPtr(new InitialData{Data=pin.AddrOfPinnedObject()},initial,false);}
            Marshal.ThrowExceptionForHR(Method<CreateBuffer>(_device,3)(_device,ref desc,initial,out var buffer));return Keep(buffer);
        }
        finally{if(initial!=IntPtr.Zero)Marshal.FreeHGlobal(initial);if(pin.IsAllocated)pin.Free();}
    }
    internal IntPtr Input(float[] values,int stride)
    {var bytes=new byte[values.Length*4];System.Buffer.BlockCopy(values,0,bytes,0,bytes.Length);var buffer=Buffer(bytes,bytes.Length,stride,8);Marshal.ThrowExceptionForHR(Method<CreateView>(_device,7)(_device,buffer,IntPtr.Zero,out var view));return Keep(view);}
    internal (IntPtr Buffer,IntPtr View) Output(int bytes,int stride)
    {var buffer=Buffer(null,bytes,stride,128);Marshal.ThrowExceptionForHR(Method<CreateView>(_device,8)(_device,buffer,IntPtr.Zero,out var view));return(buffer,Keep(view));}
    internal uint[] Run(IntPtr shader,IntPtr[] inputs,IntPtr output,IntPtr outputView,int words)
    {
        var inputPointers=Marshal.AllocHGlobal(inputs.Length*IntPtr.Size);var outputPointer=Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.Copy(inputs,0,inputPointers,inputs.Length);Marshal.WriteIntPtr(outputPointer,outputView);
            Method<SetViews>(_context,67)(_context,0,(uint)inputs.Length,inputPointers);Method<SetOutputs>(_context,68)(_context,0,1,outputPointer,IntPtr.Zero);Method<SetShader>(_context,69)(_context,shader,IntPtr.Zero,0);
            Method<Dispatch>(_context,41)(_context,(uint)((words/2+63)/64),1,1);
            var staging=Buffer(null,words*4,0,0,true);Method<CopyResource>(_context,47)(_context,staging,output);Marshal.ThrowExceptionForHR(Method<Map>(_context,14)(_context,staging,0,1,0,out var mapped));
            try{var result=new int[words];Marshal.Copy(mapped.Data,result,0,words);var unsigned=new uint[words];System.Buffer.BlockCopy(result,0,unsigned,0,words*4);return unsigned;}finally{Method<Unmap>(_context,15)(_context,staging,0);Release(staging);}
        }
        finally{Marshal.FreeHGlobal(inputPointers);Marshal.FreeHGlobal(outputPointer);}
    }
    public void Dispose(){for(int i=_resources.Count-1;i>=0;i--)Marshal.Release(_resources[i]);_resources.Clear();}
}
}
