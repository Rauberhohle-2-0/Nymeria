# HelloMaterials texture fixtures

Every file here is original project content generated for this example. No OMSI,
third-party, or trademarked asset is included, and no file is copied from an
external image library.

Each image is a 128 × 128 four-quadrant checkerboard: red top-left, green
top-right, blue bottom-left, and yellow bottom-right as seen from outside a cube
face, with an alternating brightness pattern inside each quadrant. That makes a
flipped U or V axis immediately visible.

| File | Encoding | Purpose |
| --- | --- | --- |
| `checker.png` | 8-bit RGBA PNG | Untextured baseline comparison for the other formats |
| `checker.jpg` | Baseline JPEG, no alpha | JPEG decoding; decoded pixels are opaque |
| `checker.tga` | Uncompressed 32-bit BGRA, bottom-left origin | TGA channel swap and origin-flag handling |
| `checker-rle.tga` | RLE-compressed 24-bit BGR, top-left origin | TGA RLE packets and 24-bit opaque pixels |
| `checker.bmp` | Uncompressed 24-bit BGR, bottom-up rows | BMP channel order, row padding, and bottom-up orientation |
| `checker-32.bmp` | Uncompressed 32-bit BGRA, top-down rows, opaque | BMP negative height and the reserved-alpha/BGRX rule |
| `checker-bc1.dds` | BC1, eight mip levels | Compressed DDS with a mip chain; levels below 4 × 4 are uploaded only when block aligned |
| `checker-rgba.dds` | Uncompressed 32-bit RGBA (`DDSD_PITCH`) | Uncompressed DDS channel and pitch handling |

The two BMP files are written by hand from the same checkerboard pixels as
`checker.png`. `checker.bmp` uses a 40-byte `BITMAPINFOHEADER`, positive height,
four-byte row padding, and no alpha. `checker-32.bmp` uses a 40-byte header with a
negative height, so its rows are stored top-down; its fourth byte is reserved and
the decoder therefore returns opaque pixels. There is no alpha mask in either
file, so alpha is not claimed to have been imported.

The `.dds` files use the legacy `DXT1` fourCC and uncompressed RGB masks rather
than the DX10 extension header, so they exercise the older header path. The
decoder also accepts the DX10 header; see [docs/MATERIALS.md](../../../docs/MATERIALS.md).

The test projects build equivalent TGA, DDS, and PNG files in memory, and each
file here is small enough to regenerate by hand or replace with your own image of
the same size.
