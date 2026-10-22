using System.Numerics;

namespace Nymeria.Core.Animation;

/// <summary>
/// A sampled animation pose containing only the channels the clip animates.
/// </summary>
/// <remarks>
/// A null channel means "not animated by this clip", not "reset to identity".
/// Applying a pose leaves the target transform's other channels unchanged.
/// </remarks>
public readonly record struct TransformPose(
    Vector3? Translation,
    Quaternion? Rotation,
    Vector3? Scale)
{
    /// <summary>Gets whether the pose contains a translation channel.</summary>
    public bool HasTranslation => Translation.HasValue;

    /// <summary>Gets whether the pose contains a rotation channel.</summary>
    public bool HasRotation => Rotation.HasValue;

    /// <summary>Gets whether the pose contains a scale channel.</summary>
    public bool HasScale => Scale.HasValue;
}
