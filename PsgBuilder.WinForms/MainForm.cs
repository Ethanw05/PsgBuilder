using System.Text.Json;
using System.Numerics;
using PsgBuilder.Collision;
using PsgBuilder.Core;
using PsgBuilder.Core.Rw;
using PsgBuilder.Core.Psg;
using PsgBuilder.Collision.ClusteredMesh;
using PsgBuilder.Collision.Serialization;
using PsgBuilder.Glb;
using PsgBuilder.Mesh;
using PsgBuilder.Texture;
using SharpGLTF.Schema2;

namespace PsgBuilder.WinForms;

public sealed class MainForm : Form
{
    private readonly TextBox _folderText = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly Button _browseButton = new() { Text = "Browse...", Dock = DockStyle.Right, Width = 110 };
    private readonly CheckBox _flattenAllCheck = new() { Text = "Flatten all meshes (one PSG with multiple meshes; overflow split in same file)", AutoSize = true };
    private readonly Button _buildButton = new() { Text = "BUILD PSGS", Dock = DockStyle.Top, Height = 44 };
    private readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true };

    public MainForm()
    {
        Text = "PsgBuilder – Build Mesh, Collision & Texture PSGs";
        Width = 900;
        Height = 650;

        var topRow = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(8) };
        topRow.Controls.Add(_folderText);
        topRow.Controls.Add(_browseButton);

        var optionsRow = new Panel { Dock = DockStyle.Top, Height = 28, Padding = new Padding(8, 4, 8, 4) };
        optionsRow.Controls.Add(_flattenAllCheck);

        var root = new Panel { Dock = DockStyle.Fill };
        root.Controls.Add(_log);
        root.Controls.Add(_buildButton);
        root.Controls.Add(optionsRow);
        root.Controls.Add(topRow);

        Controls.Add(root);

        _browseButton.Click += (_, _) => BrowseFolder();
        _buildButton.Click += async (_, _) => await BuildPsGsAsync();
    }

    private void BrowseFolder()
    {
        using var dlg = new FolderBrowserDialog { ShowNewFolderButton = false };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _folderText.Text = dlg.SelectedPath;
        }
    }

    private async Task BuildPsGsAsync()
    {
        string folder = _folderText.Text.Trim();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            Log("Select a valid folder first.");
            return;
        }

        _buildButton.Enabled = false;
        bool flattenAll = _flattenAllCheck.Checked;
        try
        {
            await Task.Run(() => BuildPsGs(folder, flattenAll));
        }
        finally
        {
            _buildButton.Enabled = true;
        }
    }

    private void BuildPsGs(string folder, bool flattenAll)
    {
        var glbs = Directory.GetFiles(folder, "*.glb", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (glbs.Length == 0)
        {
            Log("No .glb files found in the selected folder.");
            return;
        }

        string meshDir = Path.Combine(folder, "cPres_Global");
        string collisionDir = Path.Combine(folder, "cSim_Global");
        string textureDir = Path.Combine(folder, "cPres_Global");
        Directory.CreateDirectory(meshDir);
        Directory.CreateDirectory(collisionDir);
        Directory.CreateDirectory(textureDir);

        int maxDegree = Math.Max(1, Environment.ProcessorCount - 1);
        int createdCount = 0;

        const float meshScale = 1f;  // was 256; multiply by 1/256 for game units

        Parallel.ForEach(glbs, new ParallelOptions { MaxDegreeOfParallelism = maxDegree }, glbPath =>
        {
            string glbStem = Path.GetFileNameWithoutExtension(glbPath);
            string jsonPath = Path.ChangeExtension(glbPath, ".json");
            string meshHash = Lookup8Hash.HashStringToHex(glbStem + "_mesh");
            string collisionHash = Lookup8Hash.HashStringToHex(glbStem + "_collision");
            string meshOutPath = Path.Combine(meshDir, meshHash + ".psg");
            string collisionOutPath = Path.Combine(collisionDir, collisionHash + ".psg");

            var meshTask = Task.Run(() =>
            {
                try
                {
                    if (flattenAll)
                    {
                        BuildMeshMulti(glbPath, meshOutPath, textureDir, jsonPath, meshScale, ref createdCount);
                    }
                    else
                    {
                        var input = new MeshInputFromGlb(glbPath, meshScale);

                        var textureBuild = GlbTextureAutoBuilder.BuildFromGlb(
                            glbPath,
                            textureDir,
                            generateMipMaps: true,
                            materialsJsonPath: File.Exists(jsonPath) ? jsonPath : null,
                            materialNameOverride: input.MaterialName);
                        if (string.IsNullOrWhiteSpace(input.AttributorMaterialPath) &&
                            !string.IsNullOrWhiteSpace(textureBuild.AttributorMaterialPath))
                        {
                            input.AttributorMaterialPath = textureBuild.AttributorMaterialPath;
                            Log($"[Mesh PSG] Using Attribulator material from JSON: {input.AttributorMaterialPath}");
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
                            Log($"[Texture PSG] {tex.ChannelName} -> {tex.PsgPath} (GUID 0x{tex.Guid:X16})");
                            Interlocked.Increment(ref createdCount);
                        }
                        foreach (var warning in textureBuild.Warnings)
                        {
                            Log($"[Texture WARN] {warning}");
                        }

                        var spec = MeshPsgComposer.Compose(input);
                        using var fs = File.Create(meshOutPath);
                        GenericArenaWriter.Write(spec, fs);
                        Log($"[Mesh PSG] {meshOutPath}");
                        Interlocked.Increment(ref createdCount);
                    }
                }
                catch (Exception ex)
                {
                    Log($"ERROR mesh for {Path.GetFileName(glbPath)}: {ex.Message}");
                }
            });

            var collisionTask = Task.Run(() =>
            {
                try
                {
                    var flat = GlbMeshFlattener.Flatten(glbPath);
                    var verts = flat.Vertices;
                    var faces = flat.Faces;
                    var materialName = flat.MaterialName;

                    int surfaceId = 0;
                    IReadOnlyList<IReadOnlyList<Vector3>>? splines = null;

                    if (File.Exists(jsonPath))
                    {
                        var db = BlenroseMaterialsDb.Load(jsonPath);
                        var mat = db.TryGetMaterial(materialName) ?? db.TryGetMaterial(glbStem);
                        if (mat != null)
                        {
                            surfaceId = SurfaceIdHelper.EncodeSurfaceId(mat.Collision.AudioSurface, mat.Collision.PhysicsSurface, mat.Collision.SurfacePattern);
                            splines = mat.Splines?.Select(s => (IReadOnlyList<Vector3>)s.Points).ToList();
                        }
                    }

                    var input = new CollisionInputFromGlb(verts, faces, splines, surfaceId);
                    using var fs = File.Create(collisionOutPath);
                    var builder = new CollisionPsgBuilder
                    {
                        ForceUncompressed = false,
                        EnableVertexSmoothing = false,
                        Granularity = 0
                    };
                    builder.Build(input, fs);
                    Log($"[Collision PSG] {collisionOutPath}");
                    Interlocked.Increment(ref createdCount);
                }
                catch (Exception ex)
                {
                    Log($"ERROR collision for {Path.GetFileName(glbPath)}: {ex.Message}");
                }
            });

            Task.WaitAll(meshTask, collisionTask);
        });

        Log($"Done. Created {createdCount} PSG(s).");
    }

    /// <summary>
    /// One mesh per GLB primitive; overflow (>65536 verts) split into additional meshes in the same PSG.
    /// Matches CLI --flatten-all behavior.
    /// </summary>
    private void BuildMeshMulti(
        string glbPath,
        string meshOutPath,
        string textureDir,
        string? jsonPath,
        float meshScale,
        ref int createdCount)
    {
        var input = new MeshInputFromGlbMulti(glbPath, meshScale, reverseWinding: false);

        if (input.Parts.Count == 0)
        {
            Log($"ERROR mesh for {Path.GetFileName(glbPath)}: No geometry produced.");
            return;
        }

        var textureBuild = GlbTextureAutoBuilder.BuildFromGlb(
            glbPath,
            textureDir,
            generateMipMaps: true,
            materialsJsonPath: File.Exists(jsonPath ?? "") ? jsonPath : null,
            materialNameOverride: input.MaterialName);
        if (string.IsNullOrWhiteSpace(input.AttributorMaterialPath) &&
            !string.IsNullOrWhiteSpace(textureBuild.AttributorMaterialPath))
        {
            input.AttributorMaterialPath = textureBuild.AttributorMaterialPath;
            Log($"[Mesh PSG] Using Attribulator material from JSON: {input.AttributorMaterialPath}");
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
            Log($"[Texture PSG] {tex.ChannelName} -> {tex.PsgPath} (GUID 0x{tex.Guid:X16})");
            Interlocked.Increment(ref createdCount);
        }
        foreach (var warning in textureBuild.Warnings)
        {
            Log($"[Texture WARN] {warning}");
        }

        var spec = MeshPsgComposer.Compose(input);
        using (var fs = File.Create(meshOutPath))
            GenericArenaWriter.Write(spec, fs);
        Log($"[Mesh PSG] {meshOutPath} ({input.Parts.Count} mesh(es))");
        Interlocked.Increment(ref createdCount);
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => Log(message)));
            return;
        }
        _log.AppendText(message + Environment.NewLine);
        _log.ScrollToCaret();
    }
}


internal sealed class BlenroseMaterialsDb
{
    public sealed class CollisionInfo
    {
        public int PhysicsSurface { get; init; }
        public int AudioSurface { get; init; }
        public int SurfacePattern { get; init; }
    }

    public sealed class Spline
    {
        public string Name { get; init; } = string.Empty;
        public List<Vector3> Points { get; init; } = new();
        public bool IsClosed { get; init; }
        public string Type { get; init; } = string.Empty;
    }

    public sealed class Material
    {
        public string Name { get; init; } = string.Empty;
        public CollisionInfo Collision { get; init; } = new();
        public List<Spline>? Splines { get; init; }
    }

    private readonly Dictionary<string, Material> _materials;

    private BlenroseMaterialsDb(Dictionary<string, Material> materials) => _materials = materials;

    public static BlenroseMaterialsDb Load(string jsonPath)
    {
        using var fs = File.OpenRead(jsonPath);
        using var doc = JsonDocument.Parse(fs);
        var mats = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            string name = prop.Name;
            var root = prop.Value;

            var collision = root.GetProperty("collision");
            int physics = int.Parse(collision.GetProperty("physics_surface").GetString() ?? "0");
            int audio = int.Parse(collision.GetProperty("audio_surface").GetString() ?? "0");
            int pattern = int.Parse(collision.GetProperty("surface_pattern").GetString() ?? "0");

            List<Spline>? splines = null;
            if (root.TryGetProperty("splines", out var splinesEl) && splinesEl.ValueKind == JsonValueKind.Array)
            {
                splines = new List<Spline>();
                foreach (var s in splinesEl.EnumerateArray())
                {
                    var pts = new List<Vector3>();
                    foreach (var p in s.GetProperty("points").EnumerateArray())
                    {
                        pts.Add(new Vector3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle()));
                    }
                    splines.Add(new Spline
                    {
                        Name = s.GetProperty("name").GetString() ?? string.Empty,
                        IsClosed = s.GetProperty("is_closed").GetBoolean(),
                        Type = s.GetProperty("type").GetString() ?? string.Empty,
                        Points = pts
                    });
                }
            }

            mats[name] = new Material
            {
                Name = name,
                Collision = new CollisionInfo { PhysicsSurface = physics, AudioSurface = audio, SurfacePattern = pattern },
                Splines = splines
            };
        }

        return new BlenroseMaterialsDb(mats);
    }

    public Material? TryGetMaterial(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return _materials.TryGetValue(name, out var m) ? m : null;
    }
}

