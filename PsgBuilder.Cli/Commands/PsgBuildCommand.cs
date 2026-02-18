using PsgBuilder.Collision;
using PsgBuilder.Core;
using PsgBuilder.Core.Rw;
using PsgBuilder.Core.Psg;
using PsgBuilder.Glb;
using PsgBuilder.Mesh;
using PsgBuilder.Texture;

namespace PsgBuilder.Cli.Commands;

/// <summary>
/// Builds mesh, collision, and texture PSGs from a GLB.
/// </summary>
internal static class PsgBuildCommand
{
    public static int Run(string[] args)
    {
        float scale = 1f;  // was 256; multiply by 1/256 for game units
        bool forceUncompressed = args.Any(a => a.Equals("--force-uncompressed", StringComparison.OrdinalIgnoreCase));
        string? textureDirArg = GetOptionValue(args, "--texture-dir=");
        string? materialsJsonArg = GetOptionValue(args, "--materials-json=");
        foreach (var a in args)
        {
            if (a.StartsWith("--scale=", StringComparison.OrdinalIgnoreCase) &&
                float.TryParse(a[8..], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float s))
                scale = s;
        }
        var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

        if (positional.Length is < 1 or > 3)
            return CliErrors.Fail("Usage: psg-build <input.glb> [mesh_output.psg] [collision_output.psg] [--scale=1] [--force-uncompressed] [--texture-dir=<dir>] [--materials-json=<path>]");

        string glbPath = positional[0];
        string meshOutPath = positional.Length >= 2 ? positional[1] : GetDefaultMeshOutPath(glbPath);
        string collisionOutPath = positional.Length >= 3 ? positional[2] : GetDefaultCollisionOutPath(glbPath);
        string textureOutDir = !string.IsNullOrWhiteSpace(textureDirArg) ? textureDirArg! : GetDefaultTextureOutDir(glbPath);
        if (!string.IsNullOrWhiteSpace(materialsJsonArg) && !File.Exists(materialsJsonArg))
            return CliErrors.Fail($"Materials JSON not found: {materialsJsonArg}");

        string? materialsJsonPath = ResolveMaterialsJsonPath(glbPath, materialsJsonArg);

        if (!File.Exists(glbPath)) return CliErrors.Fail($"Input GLB not found: {glbPath}");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(meshOutPath))!);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(collisionOutPath))!);
        Directory.CreateDirectory(Path.GetFullPath(textureOutDir));

        Console.WriteLine($"Loading GLB: {glbPath}");
        Console.WriteLine("Building mesh, collision, and texture PSGs...");
        if (!string.IsNullOrWhiteSpace(materialsJsonPath))
            Console.WriteLine($"Using materials JSON for texture paths: {materialsJsonPath}");

        var meshTask = Task.Run(() => BuildMeshAndTextures(glbPath, meshOutPath, textureOutDir, scale, materialsJsonPath));
        var collisionTask = Task.Run(() => BuildCollision(glbPath, collisionOutPath, forceUncompressed));

        Task.WaitAll(meshTask, collisionTask);

        int meshResult = meshTask.Result;
        int collisionResult = collisionTask.Result;

        if (meshResult == 0) Console.WriteLine($"Wrote mesh PSG:      {meshOutPath}");
        if (collisionResult == 0) Console.WriteLine($"Wrote collision PSG: {collisionOutPath}");
        if (meshResult == 0) Console.WriteLine($"Wrote texture PSGs:  {Path.GetFullPath(textureOutDir)}");

        if (meshResult != 0 || collisionResult != 0)
            return meshResult != 0 ? meshResult : collisionResult;
        return 0;
    }

    private static int BuildMeshAndTextures(string glbPath, string outPath, string textureDir, float scale, string? materialsJsonPath)
    {
        try
        {
            var input = new MeshInputFromGlb(glbPath, scale);

            var textureBuild = GlbTextureAutoBuilder.BuildFromGlb(
                glbPath,
                textureDir,
                generateMipMaps: true,
                materialsJsonPath: materialsJsonPath,
                materialNameOverride: input.MaterialName);
            if (string.IsNullOrWhiteSpace(input.AttributorMaterialPath) &&
                !string.IsNullOrWhiteSpace(textureBuild.AttributorMaterialPath))
            {
                input.AttributorMaterialPath = textureBuild.AttributorMaterialPath;
                Console.WriteLine($"Using Attribulator material from JSON: {input.AttributorMaterialPath}");
            }
            if (textureBuild.HasOverrides)
            {
                input.TextureChannelOverrides = new RenderMaterialDataRwBuilder.MaterialTextureOverrides(
                    NameChannelGuid: textureBuild.DiffuseGuid,
                    DiffuseGuid: textureBuild.DiffuseGuid,
                    NormalGuid: textureBuild.NormalGuid,
                    LightmapGuid: textureBuild.LightmapGuid,
                    SpecularGuid: textureBuild.SpecularGuid);
            }

            foreach (var tex in textureBuild.BuiltTextures)
            {
                Console.WriteLine($"Texture [{tex.ChannelName}] => {tex.PsgPath} (GUID 0x{tex.Guid:X16})");
            }
            foreach (var warning in textureBuild.Warnings)
            {
                Console.WriteLine($"Texture warning: {warning}");
            }

            var spec = MeshPsgComposer.Compose(input);
            using (var fs = File.Create(outPath))
                GenericArenaWriter.Write(spec, fs);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Mesh build failed: {ex.Message}");
            return 1;
        }
    }

    private static int BuildCollision(string glbPath, string outPath, bool forceUncompressed)
    {
        try
        {
            var flat = GlbMeshFlattener.Flatten(glbPath);
            var input = new CollisionInputFromGlb(flat.Vertices, flat.Faces, splines: null, surfaceId: 0);
            var builder = new CollisionPsgBuilder
            {
                ForceUncompressed = forceUncompressed,
                EnableVertexSmoothing = false,
                Granularity = 0
            };
            using (var fs = File.Create(outPath))
                builder.Build(input, fs);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Collision build failed: {ex.Message}");
            return 1;
        }
    }

    private static string GetDefaultMeshOutPath(string glbPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(glbPath)) ?? ".";
        var outDir = Path.Combine(dir, "cPres_Global");
        string glbStem = Path.GetFileNameWithoutExtension(glbPath);
        string name = Lookup8Hash.HashStringToHex(glbStem + "_mesh") + ".psg";
        return Path.Combine(outDir, name);
    }

    private static string GetDefaultCollisionOutPath(string glbPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(glbPath)) ?? ".";
        var outDir = Path.Combine(dir, "cSim_Global");
        string glbStem = Path.GetFileNameWithoutExtension(glbPath);
        string name = Lookup8Hash.HashStringToHex(glbStem + "_collision") + ".psg";
        return Path.Combine(outDir, name);
    }

    private static string GetDefaultTextureOutDir(string glbPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(glbPath)) ?? ".";
        return Path.Combine(dir, "cPres_Global");
    }

    private static string? ResolveMaterialsJsonPath(string glbPath, string? materialsJsonArg)
    {
        if (!string.IsNullOrWhiteSpace(materialsJsonArg))
            return Path.GetFullPath(materialsJsonArg);

        string sidecarJson = Path.ChangeExtension(Path.GetFullPath(glbPath), ".json");
        return File.Exists(sidecarJson) ? sidecarJson : null;
    }

    private static string? GetOptionValue(IEnumerable<string> args, string optionPrefix)
    {
        foreach (var a in args)
        {
            if (a.StartsWith(optionPrefix, StringComparison.OrdinalIgnoreCase))
                return a.Substring(optionPrefix.Length);
        }
        return null;
    }
}
