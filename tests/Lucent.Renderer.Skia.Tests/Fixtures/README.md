# Raster fixtures

`pixel.png` is an original 1×1 RGBA PNG with the opaque pixel `(35, 100, 160, 255)`. Its PNG chunks and zlib payload were generated for the runtime and packaged-consumer checks; it is covered by the repository license.

The historical SDK `Assets/Artwork/tiny.png` fixture remains unchanged. It is useful for metadata and exact-byte packaging checks, but its malformed pixel payload is a negative decoder case.

The SVG text fixture Abel-Regular.ttf and Abel-OFL.txt come from Google Fonts commit 3b99d83d2625944fc0b8bd328d793fa819b92381 (ofl/abel). Font SHA-256: 8809dcad25318225052f88333e208c5aad4adcb7b2c934c135735ec19aa410b4. This explicitly configured, OFL-licensed fixture keeps SVG text tests independent of installed fonts. It is not an application default font.
