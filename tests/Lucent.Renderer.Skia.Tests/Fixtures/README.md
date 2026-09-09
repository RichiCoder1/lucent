# Raster fixtures

`pixel.png` is an original 1×1 RGBA PNG with the opaque pixel `(35, 100, 160, 255)`. Its PNG chunks and zlib payload were generated for the runtime and packaged-consumer checks; it is covered by the repository license.

The historical SDK `Assets/Artwork/tiny.png` fixture remains unchanged. It is useful for metadata and exact-byte packaging checks, but its malformed pixel payload is a negative decoder case.
