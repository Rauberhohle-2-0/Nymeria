# DDS test fixtures

These 8 × 8 files are original project content generated for the test suite. Each
contains one block-compressed mip level with a zero-filled block payload. Tests
assert header parsing, format mapping, color-space flags, and block sizing; they
do not assert decoded colors, and GPU decoding is not part of the headless tests.

| File | Source header | Expected result |
| --- | --- | --- |
| `bc1.dds` | Legacy `DXT1` | BC1, unorm, 4 blocks × 8 bytes |
| `bc2.dds` | Legacy `DXT3` | BC2, unorm, 4 blocks × 16 bytes |
| `bc3.dds` | Legacy `DXT5` | BC3, unorm, 4 blocks × 16 bytes |
| `bc4.dds` | Legacy `BC4U` | BC4, unorm, 4 blocks × 8 bytes |
| `bc5.dds` | Legacy `ATI2` | BC5, unorm, 4 blocks × 16 bytes |
| `bc7.dds` | DX10, format 98 | BC7, unorm, 4 blocks × 16 bytes |
| `bc1-srgb.dds` | DX10, format 72 | BC1, sRGB, 4 blocks × 8 bytes |

The bytes are not a compressed image of anything; do not use them as visual
assets.
