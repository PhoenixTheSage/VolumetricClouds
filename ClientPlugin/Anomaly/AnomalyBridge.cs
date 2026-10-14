using System;
using System.Collections.Generic;
using System.Reflection;
using ClientPlugin.Clouds;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Anomaly;

/// <summary>
/// Reflection-only bind to Anomaly. No compile-time reference.
/// </summary>
internal static class AnomalyBridge
{
    public const string PackId = "volumetric.clouds";
    public const string PassId = "volumetric.clouds.volume";
    public const string ShadowPassId = "volumetric.clouds.shadow";
    public const string OccludePassId = "volumetric.clouds.occlude";
    public const string StudioPassId = "volumetric.clouds.studio";
    public const string StudioResolveId = "volumetric.clouds.studio.resolve";
    public const string StudioPresentId = "volumetric.clouds.studio.present";
    public const string StudioWorldId = "volumetric.clouds.studio.world";
    public const string StudioVolumesName = "clouds.studioVolumes";
    public const string VolumeResolveId = "volumetric.clouds.volume.resolve";
    public const string UniformPassId = "volumetric.clouds.uniforms";
    static readonly string[] PassIds = { ShadowPassId, OccludePassId, PassId, VolumeResolveId };
    static readonly string[] StudioPassIds = { StudioPassId, StudioResolveId, StudioPresentId };
    public const string ShapeName = "clouds.shape";
    public const string DetailName = "clouds.detail";
    public const string WeatherName = "clouds.weather";
    public const string StormsName = "clouds.storms";

    const string PackRegistryType = "ClientPlugin.Shaders.ShaderPackRegistry";
    const string FullscreenType = "ClientPlugin.Shaders.FullscreenPassRegistry";
    const string OwnedPassType = "ClientPlugin.Shaders.OwnedPassRegistry";
    const string CatalogType = "ClientPlugin.Buffers.BufferCatalog";
    const string PublishedType = "ClientPlugin.Buffers.PublishedBuffer";
    const string PlanetAtmosphereType = "ClientPlugin.Shaders.PlanetAtmosphere";

    static readonly object Gate = new();
    static bool registered;
    static MethodInfo setUniforms;
    static MethodInfo setEnabled;
    static MethodInfo tryGetProgramStatus;
    static MethodInfo catalogActive;
    static PropertyInfo hasDisplayTenant;
    static MethodInfo catalogPublish;
    static MethodInfo catalogUnpublish;
    static object shapePublished;
    static object detailPublished;
    static object weatherPublished;
    static object stormsPublished;
    static object studioHistoryPublished;
    static object studioVolumesPublished;
    static object volumeHistoryPublished;
    static MethodInfo publishedPublish;
    static MethodInfo tryGetRadii;

    public static bool IsRegistered
    {
        get
        {
            lock (Gate)
                return registered;
        }
    }

    public static bool RegistryFound
    {
        get { lock (Gate) return setUniforms != null || registered; }
    }

    public static bool ProgramStatusAvailable
    {
        get { lock (Gate) return tryGetProgramStatus != null; }
    }

    public static string PackRoot { get; private set; }

    public static bool HasDisplayTenant
    {
        get
        {
            PropertyInfo prop;
            lock (Gate)
            {
                if (hasDisplayTenant == null)
                    ProbeDisplayTenantUnlocked();
                prop = hasDisplayTenant;
            }

            if (prop == null)
                return false;
            try
            {
                return prop.GetValue(null) is true;
            }
            catch
            {
                return false;
            }
        }
    }

    public static bool TryRegisterPack(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return false;

        lock (Gate)
        {
            if (registered)
                return true;

            Type packType = null;
            Type fullscreen = null;
            Type owned = null;
            Type catalog = null;
            Type published = null;
            Type planetAtmo = null;
            foreach (var assembly in SafeAssemblies())
            {
                packType ??= assembly.GetType(PackRegistryType, false, false);
                fullscreen ??= assembly.GetType(FullscreenType, false, false);
                owned ??= assembly.GetType(OwnedPassType, false, false);
                catalog ??= assembly.GetType(CatalogType, false, false);
                published ??= assembly.GetType(PublishedType, false, false);
                planetAtmo ??= assembly.GetType(PlanetAtmosphereType, false, false);
            }

            if (packType == null)
            {
                MyLog.Default.Warning($"{Plugin.Name}: Anomaly ShaderPackRegistry not found");
                return false;
            }

            packType.GetMethod("Register", BindingFlags.Public | BindingFlags.Static)
                ?.Invoke(null, new object[] { PackId, root });

            PackRoot = root;
            CloudTextures.SetPackRoot(root);

            setUniforms = fullscreen?.GetMethod("SetUniforms", BindingFlags.Public | BindingFlags.Static);
            setEnabled = fullscreen?.GetMethod("SetEnabled", BindingFlags.Public | BindingFlags.Static);
            tryGetProgramStatus = fullscreen?.GetMethod(
                "TryGetProgramStatus",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(string).MakeByRefType() },
                null);
            hasDisplayTenant = owned?.GetProperty("HasDisplayTenant", BindingFlags.Public | BindingFlags.Static);
            catalogActive = catalog?.GetMethod("Active", BindingFlags.Public | BindingFlags.Static);
            catalogPublish = catalog?.GetMethod("Publish", BindingFlags.Public | BindingFlags.Static);
            catalogUnpublish = catalog?.GetMethod("Unpublish", BindingFlags.Public | BindingFlags.Static);
            if (published != null)
            {
                shapePublished = Activator.CreateInstance(published);
                detailPublished = Activator.CreateInstance(published);
                weatherPublished = Activator.CreateInstance(published);
                stormsPublished = Activator.CreateInstance(published);
                studioHistoryPublished = Activator.CreateInstance(published);
                studioVolumesPublished = Activator.CreateInstance(published);
                volumeHistoryPublished = Activator.CreateInstance(published);
                publishedPublish = published.GetMethod(
                    "Publish",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(object), typeof(IntPtr), typeof(int), typeof(int) },
                    null);
            }

            tryGetRadii = planetAtmo?.GetMethod("TryGetRadii", BindingFlags.Public | BindingFlags.Static);
            catalog?.GetMethod("RegisterLifetime", BindingFlags.Public | BindingFlags.Static)
                ?.Invoke(null, new object[]
                {
                    PackId,
                    (Action)OnLifetimeChanged,
                    (Action)OnLifetimeChanged,
                });

            var register = FindOwnedRegister(owned);
            register?.Invoke(null, new object[]
            {
                UniformPassId,
                "AfterAtmosphere",
                0,
                0,
                (Action<object>)(_ => CloudRenderer.PushUniforms()),
                0,
            });

            registered = true;
            MyLog.Default.WriteLine($"{Plugin.Name}: registered Anomaly pack '{PackId}'");
            return true;
        }
    }

    public static void PublishTextures()
    {
        lock (Gate)
        {
            PublishOne(ShapeName, shapePublished, CloudTextures.Shape);
            PublishOne(DetailName, detailPublished, CloudTextures.Detail);
            PublishOne(WeatherName, weatherPublished, CloudTextures.Weather);
            PublishOne(StormsName, stormsPublished, CloudTextures.Storms);
        }
    }

    /// <summary>
    /// Slice AK extras for this world-space planet center. False when
    /// Anomaly has no snapshot or published a different planet.
    /// </summary>
    public static bool TryGetCeilings(Vector3D worldCenter, out float airTop, out float visualCeil)
    {
        airTop = 0f;
        visualCeil = 0f;
        MethodInfo method;
        lock (Gate)
        {
            if (tryGetRadii == null)
                ProbePlanetAtmosphereUnlocked();
            method = tryGetRadii;
        }

        if (method == null)
            return false;
        try
        {
            var args = new object[] { worldCenter, 64f, 0f, 0f };
            if (method.Invoke(null, args) is not true)
                return false;
            airTop = (float)args[2];
            visualCeil = (float)args[3];
            return visualCeil > 1f;
        }
        catch
        {
            return false;
        }
    }

    public static bool CatalogAvailable(string name)
    {
        MethodInfo method;
        lock (Gate)
            method = catalogActive;
        if (method == null || string.IsNullOrWhiteSpace(name))
            return false;
        try
        {
            var buf = method.Invoke(null, new object[] { name });
            if (buf == null)
                return false;
            var available = buf.GetType().GetProperty("IsAvailable", BindingFlags.Public | BindingFlags.Instance);
            return available?.GetValue(buf) is true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetProgramStatus(string programId, out string status)
    {
        status = null;
        MethodInfo method;
        lock (Gate)
            method = tryGetProgramStatus;
        if (method == null || string.IsNullOrWhiteSpace(programId))
            return false;
        try
        {
            var args = new object[] { programId, null };
            if (!(method.Invoke(null, args) is true))
                return false;
            status = args[1] as string;
            return !string.IsNullOrEmpty(status);
        }
        catch (Exception e)
        {
            MyLog.Default.Warning($"{Plugin.Name}: TryGetProgramStatus failed: {e.Message}");
            return false;
        }
    }

    public static void SetUniforms(float[] values)
    {
        MethodInfo method;
        lock (Gate)
            method = setUniforms;
        try
        {
            for (var i = 0; i < PassIds.Length; i++)
                method?.Invoke(null, new object[] { PassIds[i], values });
        }
        catch (Exception e)
        {
            MyLog.Default.Error($"{Plugin.Name}: SetUniforms failed: {e.Message}");
            RenderTraceBind.Dump("Clouds.SetUniforms", e);
        }
    }

    internal static void SetSurfaceOcclusionEnabled(bool enabled)
    { setEnabled?.Invoke(null,new object[]{OccludePassId,enabled}); }

    public static void SetPassEnabled(bool enabled)
    {
        MethodInfo method;
        lock (Gate)
            method = setEnabled;
        try
        {
            for (var i = 0; i < PassIds.Length; i++)
                method?.Invoke(null, new object[] { PassIds[i], enabled });
        }
        catch (Exception e)
        {
            MyLog.Default.Error($"{Plugin.Name}: SetEnabled failed: {e.Message}");
        }
    }

    public static void PublishHistory(string name, ICloudSrv texture)
    {
        lock (Gate)
        {
            var published = name == CloudHistory.StudioName ? studioHistoryPublished : volumeHistoryPublished;
            PublishOne(name, published, texture);
        }
    }

    public static void SetStudioEnabled(bool enabled)
    {
        for (var i = 0; i < StudioPassIds.Length; i++)
            InvokeEnabled(StudioPassIds[i], enabled);
    }

    public static void SetStudioWorldEnabled(bool enabled)
    {
        InvokeEnabled(StudioWorldId, enabled);
    }

    public static void PublishStudioVolumes(ICloudSrv texture)
    {
        lock (Gate)
            PublishOne(StudioVolumesName, studioVolumesPublished, texture);
    }

    public static void SetStudioUniforms(float[] values)
    {
        MethodInfo method;
        lock (Gate)
            method = setUniforms;
        try
        {
            for (var i = 0; i < StudioPassIds.Length; i++)
                method?.Invoke(null, new object[] { StudioPassIds[i], values });
            method?.Invoke(null, new object[] { StudioWorldId, values });
        }
        catch (Exception e)
        {
            MyLog.Default.Error($"{Plugin.Name}: studio SetUniforms failed: {e.Message}");
            RenderTraceBind.Dump("Clouds.SetStudioUniforms", e);
        }
    }

    static void InvokeEnabled(string id, bool enabled)
    {
        MethodInfo method;
        lock (Gate)
            method = setEnabled;
        try
        {
            method?.Invoke(null, new object[] { id, enabled });
        }
        catch (Exception e)
        {
            MyLog.Default.Error($"{Plugin.Name}: SetEnabled '{id}' failed: {e.Message}");
        }
    }

    static void PublishOne(string name, object published, ICloudSrv texture)
    {
        if (published == null || texture == null)
            return;
        try
        {
            string method = texture.Structured != null ? "PublishStructuredFloats"
                : texture.Mips != null && texture.Mips.Length > 1 ? "PublishVolumeMips"
                : texture.Depth > 1 ? "PublishVolumeRgba8"
                : "PublishRgba8";
            var publish = published.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance);
            if (publish == null)
                return;
            if (method == "PublishStructuredFloats")
                publish.Invoke(published, new object[] { texture.Structured, texture.Stride });
            else if (method == "PublishVolumeMips")
                publish.Invoke(published, new object[] { texture.Mips, texture.Width });
            else if (method == "PublishVolumeRgba8")
                publish.Invoke(published, new object[] { texture.Rgba, texture.Width, texture.Height, texture.Depth });
            else
                publish.Invoke(published, new object[] { texture.Rgba, texture.Width, texture.Height });
            catalogPublish?.Invoke(null, new object[] { PackId, name, published });
        }
        catch (Exception e)
        {
            MyLog.Default.Error($"{Plugin.Name}: catalog publish '{name}' failed: {e.Message}");
        }
    }

    static void OnLifetimeChanged()
    {
        CloudTextures.Invalidate();
        lock (Gate)
        {
            try
            {
                catalogUnpublish?.Invoke(null, new object[] { PackId, ShapeName });
                catalogUnpublish?.Invoke(null, new object[] { PackId, DetailName });
                catalogUnpublish?.Invoke(null, new object[] { PackId, WeatherName });
                catalogUnpublish?.Invoke(null, new object[] { PackId, StormsName });
                catalogUnpublish?.Invoke(null, new object[] { PackId, StudioVolumesName });
            }
            catch
            {
                // Anomaly already tearing down.
            }
        }
    }

    static void ProbePlanetAtmosphereUnlocked()
    {
        if (tryGetRadii != null)
            return;
        foreach (var assembly in SafeAssemblies())
        {
            Type type;
            try
            {
                type = assembly.GetType(PlanetAtmosphereType, false, false);
            }
            catch
            {
                continue;
            }

            tryGetRadii = type?.GetMethod("TryGetRadii", BindingFlags.Public | BindingFlags.Static);
            if (tryGetRadii != null)
                return;
        }
    }

    static void ProbeDisplayTenantUnlocked()
    {
        if (hasDisplayTenant != null)
            return;
        foreach (var assembly in SafeAssemblies())
        {
            Type owned;
            try
            {
                owned = assembly.GetType(OwnedPassType, false, false);
            }
            catch
            {
                continue;
            }

            hasDisplayTenant = owned?.GetProperty("HasDisplayTenant", BindingFlags.Public | BindingFlags.Static);
            if (hasDisplayTenant != null)
                return;
        }
    }

    static MethodInfo FindOwnedRegister(Type owned)
    {
        if (owned == null)
            return null;
        foreach (var method in owned.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.Name != "Register" || method.IsGenericMethodDefinition)
                continue;
            var p = method.GetParameters();
            if (p.Length == 6 &&
                p[0].ParameterType == typeof(string) &&
                p[1].ParameterType == typeof(string) &&
                p[5].ParameterType == typeof(int))
                return method;
        }

        return null;
    }

    static IEnumerable<Assembly> SafeAssemblies()
    {
        Assembly[] assemblies;
        try
        {
            assemblies = AppDomain.CurrentDomain.GetAssemblies();
        }
        catch
        {
            yield break;
        }

        foreach (var assembly in assemblies)
        {
            if (assembly != null && assembly != typeof(AnomalyBridge).Assembly)
                yield return assembly;
        }
    }
}
