#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using System.IO;
using July.Physics.Authoring.PickingProxy;

namespace July.Physics.Authoring.PickingProxy.Gpu
{
public sealed class D3D11VisibilityBackend : IVisibilityBackend
{
    private readonly string _hlsl;
    public D3D11VisibilityBackend(string hlsl){_hlsl=hlsl;}
    public static bool TryCreate(string hlsl,out IVisibilityBackend? backend,out string reason)
    {
        backend=null;
        if(!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)){reason="D3D11 acceleration is available on Windows; using the CPU backend.";return false;}
        if(!File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"d3dcompiler_47.dll"))){reason="System shader compiler unavailable; using the CPU backend.";return false;}
        try{using var device=new D3D11Compute();reason="Hardware acceleration: "+device.AdapterName;backend=new D3D11VisibilityBackend(hlsl);return true;}
        catch(COMException error){reason=$"D3D11 hardware unavailable (0x{error.HResult:X8}); using the CPU backend.";return false;}
        catch(DllNotFoundException error){reason="System graphics library unavailable: "+error.Message;return false;}
        catch(EntryPointNotFoundException error){reason="System graphics API unavailable: "+error.Message;return false;}
    }
    private static float[] Float4(Vec3[] points,int first,int count)
    {var data=new float[count*4];for(int i=0;i<count;i++)for(int a=0;a<3;a++)data[4*i+a]=(float)points[first+i][a];return data;}
    public VisibilityMask Compute(VisibilityGeometry geometry,Vec3[] origins,Vec3[] directions,CancellationToken cancellationToken,Action<string>? progress)
    {
        using var gpu=new D3D11Compute();progress?.Invoke("GPU adapter: "+gpu.AdapterName);var shader=gpu.Shader(_hlsl);var nodes=gpu.Input(geometry.Nodes,48);var triangles=gpu.Input(geometry.Triangles,48);var dirs=gpu.Input(Float4(directions,0,directions.Length),16);
        int words=(directions.Length+31)/32;var result=new uint[origins.Length*words*2];var timer=Stopwatch.StartNew();long uncertain=0;
        for(int first=0;first<origins.Length;first+=4096)
        {
            cancellationToken.ThrowIfCancellationRequested();int count=Math.Min(4096,origins.Length-first);var points=gpu.Input(Float4(origins,first,count),16);var output=gpu.Output(count*words*8,8);
            var batch=gpu.Run(shader,new[]{nodes,triangles,points,dirs},output.Buffer,output.View,count*words*2);Array.Copy(batch,0,result,first*words*2,batch.Length);
            for(int i=1;i<batch.Length;i+=2){uint bits=batch[i];while(bits!=0){bits&=bits-1;uncertain++;}}
            gpu.Release(points);gpu.Release(output.View);gpu.Release(output.Buffer);
        }
        progress?.Invoke($"GPU visibility {origins.Length} x {directions.Length} in {timer.Elapsed.TotalSeconds:F3}s; {uncertain} uncertain rays for CPU verification");
        return new VisibilityMask(result,origins.Length,directions.Length);
    }
}
}
