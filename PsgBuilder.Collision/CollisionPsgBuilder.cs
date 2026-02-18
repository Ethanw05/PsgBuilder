using PsgBuilder.Core.Psg;

namespace PsgBuilder.Collision;

/// <summary>
/// Top-level PSG build. Uses <see cref="CollisionPsgComposer"/> and <see cref="GenericArenaWriter"/>.
/// Same public API and <see cref="ICollisionInput"/> as before.
/// </summary>
public sealed class CollisionPsgBuilder
{
    public bool ForceUncompressed { get; set; }
    public bool EnableVertexSmoothing { get; set; }

    public float Granularity { get; set; } = 0.0625f;

    /// <summary>Build full PSG into stream.</summary>
    public void Build(ICollisionInput input, Stream output)
    {
        PsgArenaSpec spec = CollisionPsgComposer.Compose(input, Granularity, ForceUncompressed, EnableVertexSmoothing);
        GenericArenaWriter.Write(spec, output);
    }
}

/// <summary>Optional: provide per-face surface IDs for cluster serialization.</summary>
public interface ICollisionInputWithSurfaceIds : ICollisionInput
{
    IReadOnlyList<int>? SurfaceIds { get; }
}
