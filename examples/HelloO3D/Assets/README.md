# HelloO3D fixture

`cube.o3d` is a **synthetic** O3D fixture authored for Nymeria. It is not an OMSI
game asset and is redistributable with this repository under the project's BSD
3-Clause License.

It contains the same 2 × 2 × 2 cube as
[`HelloOBJ/Assets/cube.obj`](../../HelloOBJ/Assets/cube.obj), re-expressed in the
O3D frame and winding:

- O3D version 3, flags `0`, 16-bit triangle indices, unscrambled (`key` absent
  because the version is below 4);
- 24 vertices with positions, normals, and texture coordinates;
- 12 triangles grouped into six material subsets named `front`, `back`, `right`,
  `left`, `top`, and `bottom`;
- six materials with synthetic default values and those names;
- an identity pivot matrix.

The layout is the documented one described in [O3D.md](../../../docs/O3D.md). The
headless test `CheckedInFixtureMatchesTheSyntheticCube` rebuilds the same bytes
from independent test code and compares the parsed geometry, so the fixture has
a reproducible provenance independent of any OMSI installation or third-party
asset. It is not real-world compatibility evidence; it only exercises the
documented layout.
