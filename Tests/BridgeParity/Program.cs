using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Host = ClientPlugin.Shaders;
using Catalog = ClientPlugin.Buffers.BufferCatalog;

static class Program
{
    static int checks;
    static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
    static int Main(string[] args)
    {
        string tempRoot=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        string scratch=Path.Combine(tempRoot,"CloudBridge-"+Guid.NewGuid().ToString("N"));
        try
        {
            string game=Path.GetFullPath(args[0]);
            AppDomain.CurrentDomain.AssemblyResolve+=(_,e)=>{string path=Path.Combine(game,new AssemblyName(e.Name).Name+".dll");return File.Exists(path)?Assembly.LoadFrom(path):null;};
            Directory.CreateDirectory(scratch);
            Assembly.LoadFrom(Path.Combine(game,"VRage.Library.dll")).GetType("VRage.FileSystem.MyFileSystem",true).GetMethod("Init").Invoke(null,new object[]{scratch,scratch,"Mods",null,null});
            var log=Assembly.LoadFrom(Path.Combine(game,"VRage.dll")).GetType("VRage.Utils.MyLog",true);
            log.GetProperty("Default").SetValue(null,Activator.CreateInstance(log,new object[]{false}));
            var plugin=Assembly.LoadFrom(Path.GetFullPath(args[1]));
            var configType=plugin.GetType("ClientPlugin.Config",true);var config=configType.GetField("Current").GetValue(null);
            void Set(string name,object value){var property=configType.GetProperty(name);property.SetValue(config,property.PropertyType.IsEnum?Enum.ToObject(property.PropertyType,value):value);}
            var bridge=plugin.GetType("ClientPlugin.Anomaly.AnomalyBridge",true);
            Check((bool)bridge.GetMethod("TryRegisterPack").Invoke(null,new object[]{Path.GetFullPath(args[2])}),"register actual cloud bridge");
            Check(Host.OwnedPassRegistry.Prepare!=null && Host.OwnedPassRegistry.Slot=="AfterAtmosphere","portable preparation callback");
            Check(Catalog.Resize!=null && Catalog.DeviceEnd!=null,"resource lifetime callbacks");
            var scene=plugin.GetType("ClientPlugin.Clouds.AnomalyScene",true);
            foreach(int route in new[]{0,2})
            {
                ClientPlugin.Backend.RendererRoute.Active=route;
                Check((double)scene.GetMethod("FrameSeconds").Invoke(null,null)==0,"deferred guest clock is static");
                Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="VRage.Render11"),"guest/pending clock must not load Render11");
            }
            var renderer=plugin.GetType("ClientPlugin.Clouds.CloudRenderer",true);
            var pack=renderer.GetMethod("PackStudio",BindingFlags.Static|BindingFlags.NonPublic);
            Set("Enabled",true);Set("VolumeStudio",true);Set("StudioWind",0f);Set("StudioFunnelSpin",0f);
            Set("StudioWidth",1234f);Set("StudioDensity",.6f);Set("StudioMaxSteps",80);Set("StudioNoise",2);Set("StudioVolumeMode",1);
            ClientPlugin.Backend.RendererRoute.Active=1;
            var dx=(float[])pack.Invoke(null,new[]{config});
            ClientPlugin.Backend.RendererRoute.Active=2;
            var vk=(float[])pack.Invoke(null,new[]{config});
            Check(dx.Length==64 && dx.SequenceEqual(vk),"all static studio parameters match both routes");
            Check(vk[4]==1234f && vk[7]==.6f && vk[20]==80 && vk[40]==2 && vk[52]==1,"changed studio controls reach shader constants");
            foreach(int route in new[]{1,2})
            {
                ClientPlugin.Backend.RendererRoute.Active=route;
                Host.OwnedPassRegistry.Prepare(null);
                Check(Host.FullscreenPassRegistry.Enabled["volumetric.clouds.studio"],"studio enabled");
                Check(Host.FullscreenPassRegistry.Enabled["volumetric.clouds.studio.resolve"] && Host.FullscreenPassRegistry.Enabled["volumetric.clouds.studio.present"],"studio history and presentation enabled");
                Check(!Host.FullscreenPassRegistry.Enabled["volumetric.clouds.volume"] && !Host.FullscreenPassRegistry.Enabled["volumetric.clouds.studio.world"],"inactive planetary/empty world paths stay disabled");
                Check(Host.FullscreenPassRegistry.Uniforms["volumetric.clouds.studio"].SequenceEqual(vk),"callback publishes actual packed controls");
                var shape=Catalog.Entries["clouds.shape"];var detail=Catalog.Entries["clouds.detail"];
                Check(shape.Mips.Length==8 && shape.Mips[0].Length==128*128*128*4 && detail.Mips.Length==7,"actual noise mip chains published");
                Check(shape.Mips[0].SequenceEqual(File.ReadAllBytes(Path.Combine(args[2],"Noise","cloud_noise_shape_128_v5.bin"))),"shape upload retains shipped data");
                Check(Catalog.Targets["clouds.studioHistory"]=="64x32" && Catalog.Clears.Contains("clouds.studioHistory"),"history dimensions and initial invalid marker");
                Set("Enabled",false);Host.OwnedPassRegistry.Prepare(null);
                Check(!Host.FullscreenPassRegistry.Enabled["volumetric.clouds.studio"] && !Host.FullscreenPassRegistry.Enabled["volumetric.clouds.studio.resolve"] && !Host.FullscreenPassRegistry.Enabled["volumetric.clouds.studio.present"],"disable all studio stages");
                Set("Enabled",true);
                Catalog.Resize();Check(!Catalog.Entries.ContainsKey("clouds.shape"),"resize unpublishes retired resource");
                Host.OwnedPassRegistry.Prepare(null);Check(Catalog.Entries.ContainsKey("clouds.shape"),"resize republishes resources");
                Catalog.DeviceEnd();Check(!Catalog.Entries.ContainsKey("clouds.detail"),"device end clears publication");
                Host.OwnedPassRegistry.Prepare(null);Check(Catalog.Entries.ContainsKey("clouds.detail"),"device restoration republishes resources");
                var placer=plugin.GetType("ClientPlugin.Clouds.StudioPlacer",true);
                placer.GetMethod("Commit",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                Set("VolumeStudio",false);Host.OwnedPassRegistry.Prepare(null);
                Check(Host.FullscreenPassRegistry.Enabled["volumetric.clouds.studio.world"] && !Host.FullscreenPassRegistry.Enabled["volumetric.clouds.studio"],"placed world volumes render independently of studio preview");
                var volume=Catalog.Entries["clouds.studioVolumes"];
                Check(volume.Structured.Length==128 && volume.Stride==16 && volume.Structured[7]==1234f,"placed volume dimensions and structured upload");
                placer.GetMethod("RemoveAll").Invoke(null,null);Host.OwnedPassRegistry.Prepare(null);
                Check(!Host.FullscreenPassRegistry.Enabled["volumetric.clouds.studio.world"],"removing final placed volume disables world pass");
                Set("VolumeStudio",true);
            }
            Console.WriteLine("PASS: "+checks+" actual cloud bridge checks: studio controls, route equality, mip publication, history, disable and lifetime recovery. Recording host API; not effect-pixel or live device-loss proof.");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{if(Path.GetFullPath(scratch).StartsWith(tempRoot,StringComparison.OrdinalIgnoreCase) && Directory.Exists(scratch))Directory.Delete(scratch,true);}
    }
}
namespace ClientPlugin.Backend
{
    public static class RendererRoute
    {
        public static int Active {get;set;}
        public static bool Decided=>Active!=0;
        public static bool UseSharedBus=>Active!=0;
        public static bool UseDx11Device=>Active==1;
        public static bool UseGuestImages=>Active==2;
    }
    public static class SceneView {public static int Width=>64;public static int Height=>32;}
}
namespace ClientPlugin.Shaders
{
    public static class ShaderPackRegistry {public static void Register(string id,string root){if(!File.Exists(Path.Combine(root,"anomaly.json")))throw new Exception("manifest missing");}}
    public static class FullscreenPassRegistry
    {
        public static readonly Dictionary<string,float[]> Uniforms=new Dictionary<string,float[]>();
        public static readonly Dictionary<string,bool> Enabled=new Dictionary<string,bool>();
        public static void SetUniforms(string id,float[] data)=>Uniforms[id]=(float[])data.Clone();
        public static void SetEnabled(string id,bool enabled)=>Enabled[id]=enabled;
    }
    public static class OwnedPassRegistry
    {
        public static Action<object> Prepare;public static string Slot;
        public static void RegisterPortable(string id,string slot,int priority,int policy,Action<object> prepare,int phase){Prepare=prepare;Slot=slot;}
    }
}
namespace ClientPlugin.Buffers
{
    public sealed class PublishedBuffer
    {
        public byte[][] Mips;
        public float[] Structured;public int Stride;
        public void PublishVolumeMips(byte[][] values,int dim)=>Mips=values.Select(v=>(byte[])v.Clone()).ToArray();
        public void PublishRgba8(byte[] values,int width,int height)=>Mips=new[]{(byte[])values.Clone()};
        public void PublishVolumeRgba8(byte[] values,int width,int height,int depth)=>Mips=new[]{(byte[])values.Clone()};
        public void PublishStructuredFloats(float[] values,int stride){Structured=(float[])values.Clone();Stride=stride;}
    }
    public static class BufferCatalog
    {
        public static readonly Dictionary<string,PublishedBuffer> Entries=new Dictionary<string,PublishedBuffer>();
        public static readonly Dictionary<string,string> Targets=new Dictionary<string,string>();
        public static readonly HashSet<string> Clears=new HashSet<string>();
        public static Action Resize,DeviceEnd;
        public static void RegisterLifetime(string id,Action resize,Action deviceEnd){Resize=resize;DeviceEnd=deviceEnd;}
        public static object Active(string name)=>Entries.TryGetValue(name,out var value)?value:null;
        public static void Publish(string owner,string name,PublishedBuffer value)=>Entries[name]=value;
        public static void Unpublish(string owner,string name)=>Entries.Remove(name);
    }
    public static class CatalogImages
    {
        public static void EnsureColorTarget(string owner,string name,int w,int h,int format)=>BufferCatalog.Targets[name]=w+"x"+h;
        public static void ClearColorTarget(string name,float r,float g,float b,float a){if(a!=-1)throw new Exception("history invalid marker");BufferCatalog.Clears.Add(name);}
    }
}
