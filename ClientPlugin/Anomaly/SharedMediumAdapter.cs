using System;
using System.IO;
using System.Reflection;
using ClientPlugin.Clouds;
using VRageMath;

namespace ClientPlugin.Anomaly;

/// <summary>Optional shared-volume adapter. Legacy intervals change only on the host's frame transaction.</summary>
internal static class SharedMediumAdapter
{
    const string Id="Clouds.SharedMedium";
    static Type host;
    static bool registered,active;
    static float nearDistance;
    static float[] legacy;
    internal static float NearDistance=>nearDistance;
    static object Call(string method,params object[] args)=>host.GetMethod(method,BindingFlags.Public|BindingFlags.Static)?.Invoke(null,args);
    internal static void Publish(CloudSnapshot snapshot,float[] uniforms)
    {
        if(host==null) foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
        { host=assembly.GetType("ClientPlugin.Shaders.VolumetricMediumRegistry",false); if(host!=null) break; }
        if(host==null) return;
        if(!registered)
        {
            string file=Path.Combine(AnomalyBridge.PackRoot??"","Medium","Clouds.hlsli");
            if(!File.Exists(file)) return;
            registered=Call("Register",Id,file,new[]{AnomalyBridge.ShapeName,AnomalyBridge.DetailName,AnomalyBridge.WeatherName}) is true;
            if(!registered) return;
            Call("SetIntervalCallback",Id,(Action<float>)SetNearInterval);
        }
        active=snapshot!=null && uniforms!=null;
        if(!active) nearDistance=0;
        legacy=uniforms==null?null:(float[])uniforms.Clone();
        Call("SetEnabled",Id,active);
        if(!active) return;
        var center=snapshot.PlanetCenter; double radius=snapshot.OuterRadius;
        Call("SetOrigin",Id,center.X,center.Y,center.Z);
        // Shape UV is R*p/size+wind. Convert its advection to world metres/sec.
        float speed=Math.Max(Config.Current.WindSpeed,0),spin=Math.Max(snapshot.AngularVelocity,0);
        var r1=Vector3.Normalize(new Vector3(.8f,.6f,.02f));
        var r2=Vector3.Normalize(new Vector3(-.37f,.48f,.79f));
        var r3=Vector3.Normalize(new Vector3(.47f,-.64f,.61f));
        var motion=-uniforms[20]*(r1*(speed*.00115f+spin*.018f)+r2*(speed*.00041f+spin*.006f)+r3*(speed*.00062f));
        Call("SetParameters",Id,uniforms,new[]{center.X-radius,center.Y-radius,center.Z-radius,center.X+radius,center.Y+radius,center.Z+radius},new[]{motion.X,motion.Y,motion.Z});
    }
    static void SetNearInterval(float distance)
    {
        if(!active || legacy==null) return;
        nearDistance=Math.Max(distance,0);
        var values=(float[])legacy.Clone();
        // Uniform10.y is the legacy IsolatedMix start. Uniform15.w is the
        // fine sample spacing and must not be overwritten by interval routing.
        values[41]=nearDistance;
        AnomalyBridge.SetUniforms(values);
        // Keep the surface-facing shadow product, but stop the legacy whole-scene
        // subtraction when shared optical depth supplies fog/cloud attenuation.
        AnomalyBridge.SetSurfaceOcclusionEnabled(distance<=0);
    }
    internal static void Dispose()
    {
        if(registered) Call("Unregister",Id);
        registered=active=false;nearDistance=0;host=null;legacy=null;
    }
}
