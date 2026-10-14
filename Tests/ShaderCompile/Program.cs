using System;
using System.Collections.Generic;
using System.IO;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;

static class Program
{
    static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("usage: ShaderCompile <clouds-root> <anomaly-shaders> <game-shaders>");
            return 2;
        }

        string clouds = Path.GetFullPath(args[0]);
        string anomaly = Path.GetFullPath(args[1]);
        string game = Path.GetFullPath(args[2]);
        string[] files =
        {
            "VolumetricClouds.hlsl",
            "CloudSunShadowStamp.hlsl",
            "CloudSunShadowSub.hlsl",
            "CloudVolumeStudio.hlsl",
            "CloudStudioResolve.hlsl",
            "CloudStudioPresent.hlsl",
            "CloudStudioWorld.hlsl",
            "CloudVolumeResolve.hlsl",
        };
        var macros = new[]
        {
            new ShaderMacro("ANOMALY_FULLSCREEN", "1"),
            new ShaderMacro("ANOMALY_FULLSCREEN_SLOT_AFTERATMOSPHERE", "1"),
        };

        int compiled = 0;
        foreach (string name in files)
        {
            string file = Path.Combine(clouds, "Pack", "Fullscreen", "AfterAtmosphere", name);
            using (var include = new MultiRootInclude(
                Path.GetDirectoryName(file),
                Path.Combine(clouds, "Pack", "Inject"),
                anomaly,
                game))
            using (var result = ShaderBytecode.CompileFromFile(
                file,
                "__pixel_shader",
                "ps_5_0",
                ShaderFlags.OptimizationLevel3,
                EffectFlags.None,
                macros,
                include))
            {
                if (result.HasErrors)
                    throw new InvalidOperationException(name + ": " + result.Message);
                Console.WriteLine("PASS  " + name + " (" + result.Bytecode.Data.Length + " bytes)");
                compiled++;
            }
        }

        CompileMedium(clouds, anomaly, game);
        compiled++;
        Console.WriteLine("Compiled " + compiled + " production cloud shaders.");
        return 0;
    }

    static void CompileMedium(string clouds, string anomaly, string game)
    {
        string common = Path.Combine(anomaly, "AnomalyVolumeCommon.hlsli").Replace('\\', '/');
        string provider = Path.Combine(clouds, "Pack", "Medium", "Clouds.hlsli").Replace('\\', '/');
        string evaluate = Path.Combine(anomaly, "AnomalyVolumeEvaluate.hlsli").Replace('\\', '/');
        string kernel = Path.Combine(anomaly, "AnomalyVolumeInject.hlsl").Replace('\\', '/');
        string source =
            "#include \"" + common + "\"\n" +
            "#define EvaluateMedium VolumeProvider0\n" +
            "#define MediumUniforms VolumeUniforms[0]\n" +
            "#define MediumCameraPosition (-VolumeOrigins[0].xyz)\n" +
            "#define MediumVelocity VolumeProviderMotion[0].xyz\n" +
            "#define MediumTexture0 VolumeTexture0_0\n#define MediumRegister0 t32\n" +
            "#define MediumTexture1 VolumeTexture0_1\n#define MediumRegister1 t33\n" +
            "#define MediumTexture2 VolumeTexture0_2\n#define MediumRegister2 t34\n" +
            "#include \"" + provider + "\"\n" +
            "#undef EvaluateMedium\n#undef MediumUniforms\n#undef MediumVelocity\n#undef MediumCameraPosition\n" +
            "#undef MediumTexture0\n#undef MediumTexture1\n#undef MediumTexture2\n" +
            "#undef MediumRegister0\n#undef MediumRegister1\n#undef MediumRegister2\n" +
            "AnomalyMediumSample VolumeGetMedium(uint index,float3 p) {\n" +
            "  AnomalyMediumSample m=AnomalyEmptyMedium();\n" +
            "  if(any(p<VolumeBoundsMin[index].xyz)||any(p>VolumeBoundsMax[index].xyz)) return m;\n" +
            "  if(index==0) m=VolumeProvider0(p-VolumeOrigins[0].xyz);\n" +
            "  return m;\n}\n" +
            "#include \"" + evaluate + "\"\n" +
            "#include \"" + kernel + "\"\n";
        using (var include = new MultiRootInclude(
            Path.Combine(clouds, "Pack", "Medium"),
            Path.Combine(clouds, "Pack", "Inject"),
            anomaly,
            game))
        using (var result = ShaderBytecode.Compile(
            source,
            "__compute_shader",
            "cs_5_0",
            ShaderFlags.OptimizationLevel3,
            EffectFlags.None,
            null,
            include,
            "Clouds.SharedMedium"))
        {
            if (result.HasErrors)
                throw new InvalidOperationException("Clouds.hlsli: " + result.Message);
            Console.WriteLine("PASS  Clouds.hlsli shared medium (" + result.Bytecode.Data.Length + " bytes)");
        }
    }

    sealed class MultiRootInclude : CallbackBase, Include
    {
        readonly string[] roots;
        readonly Dictionary<Stream, string> directories = new Dictionary<Stream, string>();

        public MultiRootInclude(params string[] roots)
        {
            this.roots = roots;
        }

        public Stream Open(IncludeType type, string fileName, Stream parentStream)
        {
            var candidates = new List<string>();
            if (Path.IsPathRooted(fileName))
                candidates.Add(fileName);
            if (parentStream != null && directories.TryGetValue(parentStream, out string parent))
                candidates.Add(Path.Combine(parent, fileName));
            foreach (string root in roots)
                candidates.Add(Path.Combine(root, fileName));

            foreach (string candidate in candidates)
            {
                if (!File.Exists(candidate))
                    continue;
                string path = Path.GetFullPath(candidate);
                var stream = new MemoryStream(File.ReadAllBytes(path));
                directories[stream] = Path.GetDirectoryName(path);
                return stream;
            }

            throw new FileNotFoundException("HLSL include not found: " + fileName);
        }

        public void Close(Stream stream)
        {
            directories.Remove(stream);
            stream.Dispose();
        }
    }
}
