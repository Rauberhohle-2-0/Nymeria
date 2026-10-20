namespace Nymeria.Core.Models;

/// <summary>
/// Defines the contract between a format-specific model parser and Nymeria's
/// format-independent model data.
/// </summary>
/// <remarks>
/// <para>
/// Implement this interface in a format-specific project. Wavefront OBJ and
/// OMSI O3D parsers are planned follow-up tasks and are not part of
/// <c>Nymeria.Core</c>.
/// </para>
/// <para>
/// A parser reads from the stream's current position and must not dispose or
/// close the stream. The caller owns the stream and is responsible for its
/// lifetime.
/// </para>
/// <para>
/// A parser throws <see cref="ModelFormatException"/> for malformed or
/// unsupported input and never returns a partially valid model. I/O failures
/// may propagate as their original exceptions.
/// </para>
/// </remarks>
public interface IModelParser
{
    /// <summary>
    /// Parses one model from the supplied stream.
    /// </summary>
    /// <param name="stream">A readable stream positioned at the start of the model.</param>
    /// <returns>The validated model data.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ModelFormatException">
    /// The stream is malformed or uses an unsupported feature.
    /// </exception>
    ModelData Parse(Stream stream);
}
