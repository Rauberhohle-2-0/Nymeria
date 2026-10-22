using System.Numerics;

namespace Nymeria.Core.Animation;

/// <summary>
/// One timed point on a three-component animation track.
/// </summary>
/// <remarks>
/// The keyframe is a plain value carrier. Track constructors validate the time
/// and value and reject non-finite input; a standalone keyframe does not.
/// </remarks>
/// <param name="Time">The keyframe time in seconds.</param>
/// <param name="Value">The translation or scale at that time.</param>
public readonly record struct Vector3Keyframe(float Time, Vector3 Value);
