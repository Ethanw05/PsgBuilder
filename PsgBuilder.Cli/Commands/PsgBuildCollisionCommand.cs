using PsgBuilder.Collision;
using PsgBuilder.Core;
using PsgBuilder.Glb;

namespace PsgBuilder.Cli.Commands;

internal static class PsgBuildCollisionCommand
{
    public static int Run(string[] args)
    {
        bool forceUncompressed = args.Any(a => a.Equals("--force-uncompressed", StringComparison.OrdinalIgnoreCase));
        var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        if (positional.Length is < 1 or > 2)
            return CliErrors.Fail("Usage: psg-build-collision <input.glb> [output.psg] [--force-uncompressed]");

        string glbPath = positional[0];
        string outPath = positional.Length == 2
            ? positional[1]
            : GetDefaultCollisionOutPath(glbPath);

        if (!File.Exists(glbPath)) return CliErrors.Fail($"Input GLB not found: {glbPath}");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);

        Console.WriteLine($"Loading GLB: {glbPath}");
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

        Console.WriteLine($"Wrote PSG: {outPath}");
        return 0;
    }

    private static string GetDefaultCollisionOutPath(string glbPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(glbPath)) ?? ".";
        var outDir = Path.Combine(dir, "cSim_Global");
        string glbStem = Path.GetFileNameWithoutExtension(glbPath);
        string name = Lookup8Hash.HashStringToHex(glbStem + "_collision") + ".psg";
        return Path.Combine(outDir, name);
    }
}

