"""Regenerate Lucent's original JPEG measurement fixtures (maintainer tool only).

Requires Pillow 12.3.0. The checked-in bytes and hashes are the test inputs;
normal .NET builds and measurements do not require Python or Pillow.
"""

from pathlib import Path
import hashlib
import PIL
from PIL import Image, features

if PIL.__version__ != "12.3.0":
    raise RuntimeError("Fixture regeneration requires Pillow 12.3.0.")

output = Path(__file__).resolve().parent / "fixtures"
output.mkdir(exist_ok=True)
cases = [
    ("progressive-rgb-444-32x23.jpg", "RGB", 32, 23, True),
    ("progressive-rgb-444-650x470.jpg", "RGB", 650, 470, True),
    ("cmyk-600x397.jpg", "CMYK", 600, 397, False),
]
for name, mode, width, height, progressive in cases:
    channels = len(mode)
    pixels = bytearray(width * height * channels)
    for y in range(height):
        for x in range(width):
            offset = (y * width + x) * channels
            values = ((x * 17 + y * 3) % 256, (x * 5 + y * 11) % 256,
                      ((x // 8 + y // 8) % 2) * 255, (x + y) % 96)
            pixels[offset:offset + channels] = bytes(values[:channels])
    with Image.frombytes(mode, (width, height), bytes(pixels)) as image:
        image.save(output / name, "JPEG", quality=83, subsampling=0,
                   progressive=progressive, optimize=False)
    print(name, hashlib.sha256((output / name).read_bytes()).hexdigest().upper())

print("Pillow", PIL.__version__, "JPEG", features.version_codec("jpg"),
      "libjpeg-turbo", features.version_feature("libjpeg_turbo"))
