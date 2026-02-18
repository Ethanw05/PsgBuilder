using System.Security.Cryptography;
using System.Text;
using PsgBuilder.Collision.ClusteredMesh;
using PsgBuilder.Collision.Compression;
using PsgBuilder.Collision.Serialization;
using PsgBuilder.Core.Psg;
using PsgBuilder.Core.Rw;

namespace PsgBuilder.Collision;

/// <summary>
/// Builds <see cref="PsgArenaSpec"/> from <see cref="ICollisionInput"/>.
/// Reuses existing RW builders; TOC from <see cref="CollisionTocBuilder"/>; Subrefs = 1 + numSplines.
/// </summary>
public static class CollisionPsgComposer
{
    private static readonly uint[] TypeRegistry64 =
    {
        0x00000000, 0x00010030, 0x00010031, 0x00010032, 0x00010033, 0x00010034,
        0x00010010, 0x00EB0000, 0x00EB0001, 0x00EB0003, 0x00EB0004, 0x00EB0005,
        0x00EB0006, 0x00EB000A, 0x00EB000D, 0x00EB0019, 0x00EB0007, 0x00EB0008,
        0x00EB000C, 0x00EB0009, 0x00EB000B, 0x00EB000E, 0x00EB0011, 0x00EB000F,
        0x00EB0010, 0x00EB0012, 0x00EB0022, 0x00EB0013, 0x00EB0014, 0x00EB0015,
        0x00EB0016, 0x00EB001A, 0x00EB001C, 0x00EB001D, 0x00EB001B, 0x00EB001E,
        0x00EB001F, 0x00EB0021, 0x00EB0017, 0x00EB0020, 0x00EB0024, 0x00EB0023,
        0x00EB0025, 0x00EB0026, 0x00EB0027, 0x00EB0028, 0x00EB0029, 0x00EB0018,
        0x00EC0010, 0x00010000, 0x00010002, 0x000200EB, 0x000200EA, 0x000200E9,
        0x00020081, 0x000200E8, 0x00080002, 0x00080001, 0x00080006, 0x00080003,
        0x00080004, 0x00040006, 0x00040007, 0x0001000F
    };

    /// <summary>
    /// Composes a full collision <see cref="PsgArenaSpec"/> from input and options.
    /// </summary>
    /// <param name="input">Collision mesh and splines.</param>
    /// <param name="granularity">Cluster granularity; if &lt;= 0, computed from mesh.</param>
    /// <param name="forceUncompressed">Disable compression for ClusteredMesh.</param>
    /// <param name="enableVertexSmoothing">Apply vertex smoothing.</param>
    /// <returns>Spec ready for <see cref="GenericArenaWriter.Write"/>.</returns>
    public static PsgArenaSpec Compose(
        ICollisionInput input,
        float granularity,
        bool forceUncompressed,
        bool enableVertexSmoothing)
    {
        var verts = input.Vertices;
        var faces = input.Faces;
        if (verts == null || verts.Count == 0 || faces == null || faces.Count == 0)
            throw new InvalidOperationException("Input has no vertices or faces.");

        if (granularity <= 0)
        {
            try
            {
                granularity = DetermineOptimalGranularity.Execute(verts);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException("Mesh too large for 32-bit compression. Scale down or split.", ex);
            }
        }

        uint arenaId = ComputeArenaId(verts.Count, faces.Count);
        var boundsMin = (input.Bounds.Min.X, input.Bounds.Min.Y, input.Bounds.Min.Z);
        var boundsMax = (input.Bounds.Max.X, input.Bounds.Max.Y, input.Bounds.Max.Z);
        int vertexCount = verts.Count;

        // Build RW objects: VER, INST, VOL, CMESH, CMODEL, DMO, SPLINE
        var objects = new List<PsgObjectSpec>(8);

        objects.Add(new PsgObjectSpec(VersionDataRwBuilder.Build(), RwTypeIds.VersionData));
        // Collision PSG: dict 4 = CollisionModelData, no render model
        objects.Add(new PsgObjectSpec(
            InstanceDataRwBuilder.Build(boundsMin, boundsMax, vertexCount, encodedPtrAt0x80: 0, encodedPtrAt0x84: 4),
            RwTypeIds.InstanceData));
        objects.Add(new PsgObjectSpec(VolumeRwBuilder.Build(), RwTypeIds.Volume));

        var pipelineResult = ClusteredMeshPipeline.BuildComplete(verts, faces, granularity, enableVertexSmoothing);
        IReadOnlyList<int>? surfaceIds = input is ICollisionInputWithSurfaceIds withSurf ? withSurf.SurfaceIds : null;
        byte[] cmeshBlob = ClusteredMeshBinarySerializer.Serialize(pipelineResult, granularity, forceUncompressed, surfaceIds);
        objects.Add(new PsgObjectSpec(cmeshBlob, RwTypeIds.ClusteredMesh));

        objects.Add(new PsgObjectSpec(CollisionModelDataRwBuilder.Build(), RwTypeIds.CollisionModelData));
        objects.Add(new PsgObjectSpec(DataModelObjectRwBuilder.Build(), RwTypeIds.DmoData));
        byte[] splineData = SplineDataRwBuilder.Build(input.Splines, out int numSplines);
        objects.Add(new PsgObjectSpec(splineData, RwTypeIds.SplineData));

        // TOC from CollisionTocBuilder + DynamicTocBuilder
        PsgTocSpec tocSpec = CollisionTocBuilder.Build(numSplines, boundsMin, boundsMax, vertexCount);
        byte[] tocBytes = DynamicTocBuilder.Build(tocSpec);
        objects.Add(new PsgObjectSpec(tocBytes, RwTypeIds.TableOfContents));

        // Subrefs: Instance at dict index 1 offset 0x20; Splines at dict index 6 offset 0x10 + i*0x20
        var subrefRecords = new List<PsgSubrefRecord> { new(1, 0x20) };
        for (int i = 0; i < numSplines; i++)
            subrefRecords.Add(new PsgSubrefRecord(6, (uint)(0x10 + i * 0x20)));

        return new PsgArenaSpec
        {
            ArenaId = arenaId,
            Objects = objects,
            TypeRegistry = TypeRegistry64,
            Toc = tocSpec,
            Subrefs = new PsgSubrefSpec(subrefRecords),
            UseFileSizeAt0x44 = true,
            DictRelocIsZero = true
        };
    }

    private static uint ComputeArenaId(int vertexCount, int faceCount)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes($"{vertexCount}{faceCount}"));
        return (uint)((hash[0] << 24) | (hash[1] << 16) | (hash[2] << 8) | hash[3]);
    }
}
