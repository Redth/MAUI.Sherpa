import importlib.util
from io import BytesIO
from pathlib import Path
import struct
import unittest

from PIL import Image


spec = importlib.util.spec_from_file_location(
    "icon_generation", Path(__file__).resolve().parents[1] / "build/icons/generate.py"
)
icons = importlib.util.module_from_spec(spec)
spec.loader.exec_module(icons)


class MacIconTests(unittest.TestCase):
    def test_composer_mascot_is_unmasked_and_has_no_baked_in_backdrop(self):
        image = Image.open(BytesIO(
            icons.mac_composer_artwork(icons.Artwork("summit-guide"))
        )).convert("RGBA")
        self.assertEqual(image.size, (1024, 1024))
        self.assertEqual(image.getpixel((128, 512))[3], 0)
        self.assertEqual(image.getpixel((0, 0))[3], 0)
        self.assertEqual(image.getchannel("A").getextrema(), (0, 255))
        x1, y1, x2, y2 = image.getchannel("A").getbbox()
        self.assertGreater(x1, 0)
        self.assertGreater(y1, 0)
        self.assertLess(x2, 1024)
        self.assertLess(y2, 1024)

    def test_every_frame_has_a_colored_backdrop_behind_the_transparent_mascot(self):
        data = icons.mac_icon(icons.Artwork("summit-beanie"), icons.Artwork("summit-guide"))
        position = 8
        while position < len(data):
            _, length = struct.unpack_from(">4sI", data, position)
            image = Image.open(BytesIO(data[position + 8:position + length])).convert("RGBA")
            with self.subTest(size=image.size):
                red, _, blue, alpha = image.getpixel((image.width // 8, image.height // 2))
                self.assertGreater(alpha, 200)
                self.assertGreater(blue, red)
                self.assertEqual(image.getpixel((0, 0))[3], 0)
            position += length


if __name__ == "__main__":
    unittest.main()
