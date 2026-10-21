# HelloX fixture

`cube.x` is original project content generated for this example. It is not a
Microsoft, OMSI, or third-party model, and it contains no copied assets.

The fixture is a 2 × 2 × 2 cube centered on the origin, expressed in the text
DirectX `.x` convention:

- header `xof 0303txt 0032`;
- one `Frame Root` with an identity `FrameTransformMatrix`;
- one `Mesh Cube` with 24 corners, six quad faces, 24 normals, 24 texture
  coordinates, and a six-entry `MeshMaterialList`;
- material names `front`, `back`, `right`, `left`, `top`, and `bottom`, matching
  the `HelloOBJ` fixture so the shared example color mapping works unchanged;
- source positions and normals have Z negated (Direct3D's left-handed frame) and
  faces are listed clockwise. The parser converts both back, so `HelloX` shows
  the same cube as `HelloOBJ`;
- texture coordinates use Direct3D's top-left origin, which already matches
  Nymeria's model convention;
- the `front` material contains a `TextureFilename` naming `checker.bmp`. It is
  validated but never opened or loaded; material names are the only material
  information carried into `ModelData`.

The fixture is intentionally small and readable. `tests/Nymeria.Formats.DirectX.Tests`
also builds malformed, transformed, and boundary inputs in memory.
