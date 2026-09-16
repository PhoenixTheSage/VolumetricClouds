using System;
using System.Collections.Generic;
using System.Reflection;
using ClientPlugin.Clouds;
using VRage.Render11.Resources;
using VRage.Utils;

namespace ClientPlugin.Anomaly;

/// <summary>
/// Reflection-only bind to Anomaly. No compile-time reference.
/// </summary>
internal static class AnomalyBridge
{
    public const string PackId = "volumetric.clouds";
    public const string PassId = "volumetric.clouds.volume";
    public const string UniformPassId = "volumetric.clouds.uniforms";
    public const string ShapeName = "clouds.shape";
    public const string DetailName = "clouds.detail";
    public const string WeatherName = "clouds.weather";

    const string PackRegistryType = "ClientPlugin.Shaders.ShaderPackRegistry";
    const string FullscreenType = "ClientPlugin.Shaders.FullscreenPassRegistry";
    const string OwnedPassType = "ClientPlugin.Shaders.OwnedPassRegistry";
    const string CatalogType = "ClientPlugin.Buffers.BufferCatalog";
    const string PublishedType = "ClientPlugin.Buffers.PublishedBuffer";

    static readonly object Gate = new();
    static bool registered;
    static MethodInfo setUniforms;
    static MethodInfo setEnabled;
    static PropertyInfo hasDisplayTenant;
    static MethodInfo catalogPublish;
    static MethodInfo catalogUnpublish;
    static object shapePublished;
    static object detailPublished;
    static object weatherPublished;
    static MethodInfo publishedPublish;

    public static bool IsRegistered
    {
        get
        {
            lock (Gate)
                return registered;
        }
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
            foreach (var assembly in SafeAssemblies())
            {
                packType ??= assembly.GetType(PackRegistryType, false, false);
                fullscreen ??= assembly.GetType(FullscreenType, false, false);
                owned ??= assembly.GetType(OwnedPassType, false, false);
                catalog ??= assembly.GetType(CatalogType, false, false);
                published ??= assembly.GetType(PublishedType, false, false);
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
            hasDisplayTenant = owned?.GetProperty("HasDisplayTenant", BindingFlags.Public | BindingFlags.Static);
            catalogPublish = catalog?.GetMethod("Publish", BindingFlags.Public | BindingFlags.Static);
            catalogUnpublish = catalog?.GetMethod("Unpublish", BindingFlags.Public | BindingFlags.Static);
            if (published != null)
            {
                shapePublished = Activator.CreateInstance(published);
                detailPublished = Activator.CreateInstance(published);
                weatherPublished = Activator.CreateInstance(published);
                publishedPublish = published.GetMethod("Publish", BindingFlags.Public | BindingFlags.Instance);
            }

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
        }
    }

    public static void SetUniforms(float[] values)
    {
        MethodInfo method;
        lock (Gate)
            method = setUniforms;
        try
        {
            method?.Invoke(null, new object[] { PassId, values });
        }
        catch (Exception e)
        {
            MyLog.Default.Error($"{Plugin.Name}: SetUniforms failed: {e.Message}");
            RenderTraceBind.Dump("Clouds.SetUniforms", e);
        }
    }

    public static void SetPassEnabled(bool enabled)
    {
        MethodInfo method;
        lock (Gate)
            method = setEnabled;
        try
        {
            method?.Invoke(null, new object[] { PassId, enabled });
        }
        catch (Exception e)
        {
            MyLog.Default.Error($"{Plugin.Name}: SetEnabled failed: {e.Message}");
        }
    }

    static void PublishOne(string name, object published, ISrvBindable texture)
    {
        if (published == null || publishedPublish == null || catalogPublish == null || texture == null)
            return;
        try
        {
            var native = texture.Resource != null ? texture.Resource.NativePointer : IntPtr.Zero;
            publishedPublish.Invoke(published, new object[]
            {
                texture,
                native,
                texture.Size.X,
                texture.Size.Y,
            });
            catalogPublish.Invoke(null, new object[] { PackId, name, published });
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
            }
            catch
            {
                // Anomaly already tearing down.
            }
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
