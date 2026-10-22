using System.Numerics;

namespace Nymeria.Core.Animation;

/// <summary>
/// One timed rotation on a quaternion animation track.
/// </summary>
/// <remarks>
/// The keyframe is a plain value carrier. Track constructors validate and
/// normalize the quaternion and reject non-finite or zero-length input; a
/// standalone keyframe does not.
/// </remarks>
/// <param name="Time">The keyframe time in seconds.</param>
/// <param name="Value">The rotation at that time, normalized by the track.</param>
public readonly record struct QuaternionKeyframe(float Time, Quaternion Value);
