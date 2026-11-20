import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

from PIL import Image

spec = importlib.util.spec_from_file_location(
    "screenshot_export", Path(__file__).resolve().parents[1] / "build" / "export-screenshots.py")
exporter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(exporter)


class ScreenshotExportTests(unittest.TestCase):
    def test_gallery_assets_match_recorded_dimensions_and_windows_have_no_desktop_edge(self):
        website = Path(__file__).resolve().parents[1] / "website"
        manifest = json.loads((website / "screenshots.json").read_text())
        for shot in manifest["shots"]:
            for platform, source in shot["assets"].items():
                with self.subTest(shot=shot["id"], platform=platform), Image.open(website / source) as image:
                    capture = manifest["platforms"][platform]["capture"]
                    self.assertEqual(image.size, (capture["width"], capture["height"]))
                    if platform == "windows":
                        self.assertEqual(image.mode, "RGBA")
                        alpha = image.getchannel("A")
                        self.assertIsNone(alpha.crop((0, 0, image.width, 1)).getbbox())
                        self.assertIsNone(alpha.crop((0, image.height - 1, image.width, image.height)).getbbox())
                        self.assertIsNone(alpha.crop((0, 0, 1, image.height)).getbbox())
                        self.assertIsNone(alpha.crop((image.width - 1, 0, image.width, image.height)).getbbox())

    def test_windows_desktop_fringe_is_transparent_without_changing_content(self):
        for scale in (1, 2):
            image = Image.new("RGBA", (144 * scale, 96 * scale), (44, 44, 44, 255))
            for x in range(image.width):
                image.putpixel((x, 0), (132, 167, 192, 255))
            result = exporter.clean_windows_frame(image, scale)
            for corner in ((0, 0), (result.width - 1, 0),
                           (0, result.height - 1), (result.width - 1, result.height - 1)):
                self.assertEqual(result.getpixel(corner)[3], 0)
            self.assertEqual(result.getpixel((result.width // 2, 0))[3], 0)
            self.assertEqual(result.getpixel((20 * scale, 20 * scale)), (44, 44, 44, 255))
            self.assertTrue(any(0 < alpha < 255 for alpha in result.getchannel("A").tobytes()))
            pixels = result.load()
            self.assertTrue(all(len(set(pixels[x, y][:3])) == 1
                                for y in range(result.height) for x in range(result.width)
                                if pixels[x, y][3]))

    def test_native_dimensions_and_alpha_survive_lossless_export(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "native.png"
            destination = Path(directory) / "native.webp"
            image = Image.new("RGBA", (288, 192), (43, 35, 67, 255))
            image.putpixel((0, 0), (43, 35, 67, 72))
            image.save(source)
            exporter.export_image(source, destination, "macos", logical_size=(144, 96))
            with Image.open(destination) as exported:
                self.assertEqual(exported.size, image.size)
                self.assertEqual(exported.tobytes(), image.tobytes())

    def test_one_x_source_is_rejected_instead_of_upscaled(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "legacy.png"
            destination = Path(directory) / "native.webp"
            Image.new("RGB", (144, 96)).save(source)
            with self.assertRaisesRegex(ValueError, "Recapture at native display scale"):
                exporter.export_image(source, destination, "linux", logical_size=(144, 96))
            self.assertFalse(destination.exists())
