using PsgBuilder.Cli.Commands;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                PrintHelp();
                return 0;
            }

            string cmd = args[0].ToLowerInvariant();
            var rest = args.Skip(1).ToArray();
            return cmd switch
            {
                "psg-info" => PsgInfoCommand.Run(rest),
                "psg-diff" => PsgDiffCommand.Run(rest),
                "psg-validate-cmesh" => PsgValidateClusteredMeshCommand.Run(rest),
                "psg-build" => PsgBuildCommand.Run(rest),
                "psg-build-collision" => PsgBuildCollisionCommand.Run(rest),
                "psg-build-mesh" => PsgBuildMeshCommand.Run(rest),
                "psg-build-textures" => PsgBuildTexturesCommand.Run(rest),
                _ => CliErrors.Fail($"Unknown command '{args[0]}'.")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            PsgBuilder.Cli

            Commands:
              psg-info <path>
                Prints PSG header and arena dictionary entries.

              psg-diff <pathA> <pathB>
                Compares two PSGs by arena dictionary objects (typeId/size/hash) and reports first mismatch per object.

              psg-validate-cmesh <path>
                Parses ClusteredMesh clusters and validates that all unit vertex indices are within [0, numVertices).

              psg-build <input.glb> [mesh_output.psg] [collision_output.psg] [--scale=1] [--force-uncompressed] [--texture-dir=<dir>] [--materials-json=<path>]
                Builds mesh, collision, and texture PSGs from a GLB.
                Texture PSGs are auto-generated from GLB images (PNG/JPG -> DDS -> PSG) and mesh GUIDs are linked automatically.
                If materials JSON is provided (or sidecar <input>.json exists), textures.*.image_path overrides are used first.
                Default output folders: mesh+texture -> <input_dir>\cPres_Global, collision -> <input_dir>\cSim_Global.

              psg-build-collision <input.glb> [output.psg] [--force-uncompressed]
                Builds a collision PSG from a GLB using the collision pipeline (no JSON).
                If output.psg is omitted, writes to: <input_dir>\cSim_Global\<hash>.psg

              psg-build-mesh <input.glb> [output.psg] [--scale=1] [--texture-dir=<dir>] [--materials-json=<path>]
                Builds a mesh PSG and auto-builds linked texture PSGs from GLB textures.
                If materials JSON is provided (or sidecar <input>.json exists), textures.*.image_path overrides are used first.
                If output.psg is omitted, writes to: <input_dir>\cPres_Global\<hash>.psg

              psg-build-textures <input.{dds|png|jpg|jpeg}> [output.psg] [--guid=0xGUID] [--no-mips]
                Builds a PS3 texture PSG from DDS, or converts PNG/JPG to DDS DXT5 first. GUID from texture key (filename stem).
                If output.psg is omitted, writes to: <input_dir>\<guid>.psg
            """);
    }
}

