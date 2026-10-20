namespace Nymeria.Core.Models;

/// <summary>
/// A validated, format-independent model made of one or more meshes.
/// </summary>
/// <remarks>
/// The model contains geometry and opaque material names only. It does not
/// contain transforms, a scene graph, materials, textures, animation, or
/// renderer state. See <c>docs/MODEL_DATA.md</c> for the data contract and
/// coordinate conventions.
/// </remarks>
public sealed class ModelData
{
    private readonly IReadOnlyList<MeshData> _meshes;

    /// <summary>
    /// Creates a model from already validated meshes.
    /// </summary>
    /// <param name="name">
    /// A non-empty source or model name used for diagnostics.
    /// </param>
    /// <param name="meshes">
    /// The meshes in the model. Must contain at least one non-null mesh.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty or whitespace.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="meshes"/> is null.
    /// </exception>
    /// <exception cref="ModelFormatException">
    /// <paramref name="meshes"/> is empty or contains a null element.
    /// </exception>
    public ModelData(string name, IReadOnlyList<MeshData> meshes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(meshes);

        if (meshes.Count == 0)
        {
            throw new ModelFormatException("A model must contain at least one mesh.");
        }

        var copy = new MeshData[meshes.Count];

        for (int i = 0; i < meshes.Count; i++)
        {
            copy[i] = meshes[i] ??
                throw new ModelFormatException($"Model mesh {i} is null.");
        }

        Name = name;
        _meshes = Array.AsReadOnly(copy);
    }

    /// <summary>
    /// Gets the source or model name used for diagnostics.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the meshes in this model.
    /// </summary>
    public IReadOnlyList<MeshData> Meshes => _meshes;
}
