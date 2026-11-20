#!/usr/bin/env python3
import argparse
from functools import cached_property
from io import BytesIO
from pathlib import Path
import shutil
import struct
import subprocess

from PIL import Image


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
APP = "src/MauiSherpa"
GTK = "src/MauiSherpa.LinuxGtk/Resources"
SETS = {
    "classic": ("classic", "classic"),
    "beanie": ("summit-beanie", "summit-beanie"),
    "guide": ("summit-guide", "summit-guide"),
    "mixed": ("summit-beanie", "summit-guide"),
}
WINDOWS_SIZES = (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
MAC_FRAMES = (
    (b"icp4", 16, 1), (b"ic11", 16, 2),
    (b"icp5", 32, 1), (b"ic12", 32, 2),
    (b"ic07", 128, 1), (b"ic13", 128, 2),
    (b"ic08", 256, 1), (b"ic14", 256, 2),
    (b"ic09", 512, 1), (b"ic10", 512, 2),
)


class Artwork:
    def __init__(self, name):
        self.name = name

    @cached_property
    def source(self):
        if self.name == "classic":
            return (HERE / "classic.png").read_bytes()
        return subprocess.check_output(["rsvg-convert", str(HERE / f"{self.name}.svg")])

    @cached_property
    def image(self):
        return Image.open(BytesIO(self.source)).convert("RGBA")

    @cached_property
    def windows_box(self):
        # Match the original taskbar framing: fit the visible artwork into 95% of the square.
        alpha = self.image.getchannel("A")
        x1, y1, x2, y2 = alpha.point(lambda value: 255 if value > 16 else 0).getbbox()
        half = max(x2 - x1, y2 - y1) / 1.9
        x, y = (x1 + x2) / 2, (y1 + y2) / 2
        return (x - half, y - half, x + half, y + half)

    def png(self, size, *, windows=False):
        if not windows and self.image.size == (size, size):
            return self.source
        box = self.windows_box if windows else None
        scaled = self.image.resize((size, size), Image.Resampling.LANCZOS, box=box)
        output = BytesIO()
        scaled.save(output, format="PNG", optimize=True)
        return output.getvalue()


def windows_icon(small, large):
    entries, frames = [], []
    offset = 6 + 16 * len(WINDOWS_SIZES)
    for size in WINDOWS_SIZES:
        frame = (small if size <= 48 else large).png(size, windows=True)
        dim = size % 256
        entries.append(struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(frame), offset))
        frames.append(frame)
        offset += len(frame)
    return struct.pack("<HHH", 0, 1, len(frames)) + b"".join(entries + frames)


def mac_icon(small, large):
    frames = []
    for kind, points, scale in MAC_FRAMES:
        # 32pt at 2x is still a small icon; choose by logical size, not pixel count.
        png = (small if points <= 32 else large).png(points * scale)
        frames.append(struct.pack(">4sI", kind, len(png) + 8) + png)
    return struct.pack(">4sI", b"icns", 8 + sum(map(len, frames))) + b"".join(frames)


def assets(icon_set):
    small_name, large_name = SETS[icon_set]
    small = Artwork(small_name)
    large = small if small_name == large_name else Artwork(large_name)
    large_png = large.png(1024)
    return {
        f"{APP}/Resources/AppIcon/appicon.png": large_png,
        f"{APP}/Resources/Images/sherpalogo.png": large_png,
        f"{APP}/Resources/Images/sherpaicon.png": small.png(96),
        f"{APP}/Platforms/Windows/sherpa.ico": windows_icon(small, large),
        "src/MauiSherpa.MacOS/Resources/AppIcon.icns": mac_icon(small, large),
        f"{GTK}/appicon-24.png": small.png(24),
        f"{GTK}/appicon-512.png": large.png(512),
        "website/images/sherpa-icon.png": small.png(64),
        "website/images/maui.sherpa.logo.png": large_png,
        "docs/maui.sherpa.logo.png": large_png,
    }


def main():
    parser = argparse.ArgumentParser(description="Generate Sherpa's app and website icons.")
    parser.add_argument("--check", action="store_true", help="Report stale assets without rewriting them.")
    args = parser.parse_args()
    icon_set = (HERE / "icon-set.txt").read_text(encoding="utf-8").strip()
    if icon_set not in SETS:
        parser.error(f"Invalid icon set {icon_set!r} in icon-set.txt. Choose: {', '.join(SETS)}.")
    if icon_set != "classic" and not shutil.which("rsvg-convert"):
        parser.error("rsvg-convert (librsvg) is required to render the SVG artwork.")

    changed = []
    for relative_path, data in assets(icon_set).items():
        path = ROOT / relative_path
        if not path.is_file() or path.read_bytes() != data:
            changed.append(relative_path)
            if not args.check:
                path.write_bytes(data)
    if args.check and changed:
        parser.exit(1, "Stale icons:\n" + "\n".join(changed) + "\nRun python3 build/icons/generate.py.\n")
    print(f"{icon_set}: {'assets are up to date' if args.check else f'updated {len(changed)} assets'}")


if __name__ == "__main__":
    main()
