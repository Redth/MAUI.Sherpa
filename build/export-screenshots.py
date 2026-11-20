#!/usr/bin/env python3
"""Export native screenshots as lossless WebP without resizing. Requires Pillow."""
import argparse
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw


def clean_windows_frame(image, pixel_ratio):
    """Remove the compositor desktop fringe around the Windows 11 window."""
    image = image.convert("RGBA")
    width, height = image.size
    inset = pixel_ratio
    radius = 8 * pixel_ratio
    samples = 4
    mask = Image.new("L", (width * samples, height * samples))
    ImageDraw.Draw(mask).rounded_rectangle(
        (inset * samples, inset * samples,
         (width - inset) * samples - 1, (height - inset) * samples - 1),
        radius=radius * samples, fill=255)
    mask = mask.resize(image.size, Image.Resampling.BOX)
    alpha = ImageChops.multiply(image.getchannel("A"), mask)
    # Partially covered compositor pixels already contain desktop color. Replace
    # only those edge colors with the adjacent window surface before masking.
    for y in range(height):
        xs = range(width) if y < inset + radius or y >= height - inset - radius else (0, width - 1)
        for x in xs:
            coverage = mask.getpixel((x, y))
            if 0 < coverage < 255:
                inner_x = min(max(x, inset + radius), width - inset - radius - 1)
                inner_y = min(max(y, inset + radius), height - inset - radius - 1)
                image.putpixel((x, y), image.getpixel((inner_x, inner_y)))
    image.putalpha(alpha)
    return image


def export_image(source, destination, platform, pixel_ratio=2, logical_size=(1440, 960)):
    with Image.open(source) as original:
        expected = tuple(value * pixel_ratio for value in logical_size)
        if original.size != expected:
            raise ValueError(f"{source}: expected {expected} for {pixel_ratio}x, got {original.size}. "
                             "Recapture at native display scale; do not upscale.")
        image = original.convert("RGBA")
    if platform == "windows":
        image = clean_windows_frame(image, pixel_ratio)
    destination.parent.mkdir(parents=True, exist_ok=True)
    image.save(destination, "WEBP", lossless=True, exact=True, method=6)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--platform", choices=("macos", "windows", "linux"), required=True)
    parser.add_argument("--pixel-ratio", type=int, choices=(1, 2, 3), default=2,
                        help="Native source scale; 1 is only for cleaning existing legacy assets.")
    parser.add_argument("--width", type=int, default=1440)
    parser.add_argument("--height", type=int, default=960)
    args = parser.parse_args()
    sources = sorted(path for path in args.input.iterdir() if path.suffix.lower() in (".png", ".webp"))
    if not sources:
        parser.error("The input directory contains no PNG or WebP captures.")
    if args.width <= 0 or args.height <= 0:
        parser.error("Logical dimensions must be positive.")
    for source in sources:
        destination = args.output / f"{source.stem}.webp"
        export_image(source, destination, args.platform, args.pixel_ratio, (args.width, args.height))
        print(f"Exported {destination}")


if __name__ == "__main__":
    main()
