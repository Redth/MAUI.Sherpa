#!/usr/bin/env python3
"""Capture the complete native GTK window of a Sherpa process on X11.

The GTK DevFlow agent screenshots only MAUI content, omitting the native header
bar. This captures the top-level X window, including its header bar, and crops
the client-side shadow reported by _GTK_FRAME_EXTENTS. Run the app with
GDK_BACKEND=x11 (XWayland is fine). Requires Python 3 and Pillow.
"""
import argparse
import ctypes
import os
import sys

from PIL import Image

Atom = ctypes.c_ulong
Window = ctypes.c_ulong


class XImage(ctypes.Structure):
    _fields_ = [
        ("width", ctypes.c_int), ("height", ctypes.c_int),
        ("xoffset", ctypes.c_int), ("format", ctypes.c_int),
        ("data", ctypes.c_void_p), ("byte_order", ctypes.c_int),
        ("bitmap_unit", ctypes.c_int), ("bitmap_bit_order", ctypes.c_int),
        ("bitmap_pad", ctypes.c_int), ("depth", ctypes.c_int),
        ("bytes_per_line", ctypes.c_int), ("bits_per_pixel", ctypes.c_int),
        ("red_mask", ctypes.c_ulong), ("green_mask", ctypes.c_ulong),
        ("blue_mask", ctypes.c_ulong),
    ]


class XWindowAttributes(ctypes.Structure):
    _fields_ = [
        ("x", ctypes.c_int), ("y", ctypes.c_int),
        ("width", ctypes.c_int), ("height", ctypes.c_int),
        ("border_width", ctypes.c_int), ("depth", ctypes.c_int),
        ("visual", ctypes.c_void_p), ("root", Window),
        ("class_", ctypes.c_int), ("bit_gravity", ctypes.c_int),
        ("win_gravity", ctypes.c_int), ("backing_store", ctypes.c_int),
        ("backing_planes", ctypes.c_ulong), ("backing_pixel", ctypes.c_ulong),
        ("save_under", ctypes.c_int), ("colormap", ctypes.c_ulong),
        ("map_installed", ctypes.c_int), ("map_state", ctypes.c_int),
        ("all_event_masks", ctypes.c_long), ("your_event_mask", ctypes.c_long),
        ("do_not_propagate_mask", ctypes.c_long),
        ("override_redirect", ctypes.c_int), ("screen", ctypes.c_void_p),
    ]


x = ctypes.CDLL("libX11.so.6")
x.XOpenDisplay.argtypes = [ctypes.c_char_p]
x.XOpenDisplay.restype = ctypes.c_void_p
x.XDefaultRootWindow.argtypes = [ctypes.c_void_p]
x.XDefaultRootWindow.restype = Window
x.XInternAtom.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int]
x.XInternAtom.restype = Atom
x.XQueryTree.argtypes = [
    ctypes.c_void_p, Window, ctypes.POINTER(Window), ctypes.POINTER(Window),
    ctypes.POINTER(ctypes.POINTER(Window)), ctypes.POINTER(ctypes.c_uint),
]
x.XGetWindowProperty.argtypes = [
    ctypes.c_void_p, Window, Atom, ctypes.c_long, ctypes.c_long, ctypes.c_int, Atom,
    ctypes.POINTER(Atom), ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_ulong),
    ctypes.POINTER(ctypes.c_ulong), ctypes.POINTER(ctypes.c_void_p),
]
x.XGetWindowAttributes.argtypes = [ctypes.c_void_p, Window, ctypes.POINTER(XWindowAttributes)]
x.XGetImage.argtypes = [
    ctypes.c_void_p, Window, ctypes.c_int, ctypes.c_int,
    ctypes.c_uint, ctypes.c_uint, ctypes.c_ulong, ctypes.c_int,
]
x.XGetImage.restype = ctypes.POINTER(XImage)
x.XDestroyImage.argtypes = [ctypes.POINTER(XImage)]
x.XFree.argtypes = [ctypes.c_void_p]


def cardinals(display, window, name):
    actual_type, actual_format = Atom(), ctypes.c_int()
    count, remaining, data = ctypes.c_ulong(), ctypes.c_ulong(), ctypes.c_void_p()
    atom = x.XInternAtom(display, name.encode(), 1)
    if not atom or x.XGetWindowProperty(
            display, window, atom, 0, 16, 0, 0, ctypes.byref(actual_type), ctypes.byref(actual_format),
            ctypes.byref(count), ctypes.byref(remaining), ctypes.byref(data)) != 0 or not data.value:
        return None
    try:
        if actual_format.value != 32:
            return None
        return list(ctypes.cast(data, ctypes.POINTER(ctypes.c_ulong))[:count.value])
    finally:
        x.XFree(data)


def find_window(display, pid):
    matches = []
    # Tooltips and popovers are separate override windows; only top-level normal windows count.
    normal = x.XInternAtom(display, b"_NET_WM_WINDOW_TYPE_NORMAL", 0)

    def visit(window):
        root, parent = Window(), Window()
        children, count = ctypes.POINTER(Window)(), ctypes.c_uint()
        if not x.XQueryTree(display, window, ctypes.byref(root), ctypes.byref(parent),
                            ctypes.byref(children), ctypes.byref(count)):
            return
        try:
            ids = [children[i] for i in range(count.value)]
        finally:
            if children:
                x.XFree(children)
        for child in ids:
            attributes = XWindowAttributes()
            x.XGetWindowAttributes(display, child, ctypes.byref(attributes))
            if (attributes.map_state == 2 and cardinals(display, child, "_NET_WM_PID") == [pid]
                    and attributes.width > 1 and attributes.height > 1
                    and cardinals(display, child, "_NET_WM_WINDOW_TYPE") in (None, [normal])):
                matches.append((child, attributes))
            visit(child)

    visit(x.XDefaultRootWindow(display))
    if len(matches) != 1:
        raise RuntimeError(f"Expected one visible window for process {pid}, found {len(matches)}. "
                           "Close dialogs and run the app with GDK_BACKEND=x11.")
    return matches[0]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    if not os.environ.get("DISPLAY"):
        raise RuntimeError("DISPLAY is not set; GTK window capture requires X11 or XWayland.")
    display = x.XOpenDisplay(None)
    if not display:
        raise RuntimeError("Could not open the X display.")
    window, attributes = find_window(display, args.pid)
    left, right, top, bottom = cardinals(display, window, "_GTK_FRAME_EXTENTS") or (0, 0, 0, 0)
    width = attributes.width - left - right
    height = attributes.height - top - bottom
    image = x.XGetImage(display, window, left, top, width, height, 0xFFFFFFFFFFFFFFFF, 2)
    if not image:
        raise RuntimeError("Could not read the GTK window image.")
    try:
        data = image.contents
        if data.bits_per_pixel != 32:
            raise RuntimeError(f"Unsupported {data.bits_per_pixel}-bit window image.")
        raw = ctypes.string_at(data.data, data.bytes_per_line * data.height)
        size = (data.width, data.height)
        if attributes.depth == 32:
            result = Image.frombytes("RGBa", size, raw, "raw", "BGRa", data.bytes_per_line).convert("RGBA")
        else:
            result = Image.frombytes("RGB", size, raw, "raw", "BGRX", data.bytes_per_line)
        result.save(args.output)
    finally:
        x.XDestroyImage(image)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"Error: {error}", file=sys.stderr)
        sys.exit(1)
