#!/usr/bin/env python3
"""Capture the foreground Windows compositor frame for an isolated Sherpa process."""
import argparse
import ctypes
from ctypes import wintypes

from PIL import ImageGrab


class MonitorInfo(ctypes.Structure):
    _fields_ = [
        ("size", wintypes.DWORD),
        ("monitor", wintypes.RECT),
        ("work", wintypes.RECT),
        ("flags", wintypes.DWORD),
    ]


def validate_window(expected_pid, foreground_pid, frame, work_area):
    if foreground_pid != expected_pid:
        raise RuntimeError("The isolated capture window is not in the foreground.")
    left, top, right, bottom = frame
    work_left, work_top, work_right, work_bottom = work_area
    if (left >= right or top >= bottom or left < work_left or top < work_top
            or right > work_right or bottom > work_bottom):
        raise RuntimeError("The capture window does not fit within the display's usable area.")


def window_placement(outer, frame, work_area, width, height):
    work_left, work_top, work_right, work_bottom = work_area
    work_width, work_height = work_right - work_left, work_bottom - work_top
    if width <= 0 or height <= 0 or width > work_width or height > work_height:
        raise RuntimeError("The requested capture frame does not fit within the display's usable area.")
    outer_width = width + (outer[2] - outer[0]) - (frame[2] - frame[0])
    outer_height = height + (outer[3] - outer[1]) - (frame[3] - frame[1])
    if outer_width <= 0 or outer_height <= 0:
        raise RuntimeError("The capture window has invalid frame dimensions.")
    left = work_left + (work_width - width) // 2 - (frame[0] - outer[0])
    top = work_top + (work_height - height) // 2 - (frame[1] - outer[1])
    return left, top, outer_width, outer_height


def find_window(user32, pid):
    callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    user32.EnumWindows.argtypes = [callback_type, wintypes.LPARAM]
    user32.EnumWindows.restype = wintypes.BOOL
    windows = []

    @callback_type
    def visit(window, _):
        window_pid = wintypes.DWORD()
        if (user32.IsWindowVisible(window) and not user32.GetWindow(window, 4)
                and user32.GetWindowThreadProcessId(window, ctypes.byref(window_pid))
                and window_pid.value == pid):
            windows.append(window)
        return True

    if not user32.EnumWindows(visit, 0):
        raise ctypes.WinError(ctypes.get_last_error())
    if len(windows) != 1:
        raise RuntimeError(f"Expected one visible capture window for process {pid}, found {len(windows)}.")
    return windows[0]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pid", type=int, required=True)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--output")
    mode.add_argument("--prepare", action="store_true")
    parser.add_argument("--width", type=int)
    parser.add_argument("--height", type=int)
    parser.add_argument("--pixel-ratio", type=float)
    args = parser.parse_args()
    if args.prepare and (args.width is None or args.height is None or args.pixel_ratio is None):
        parser.error("--prepare requires --width, --height, and --pixel-ratio")

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    dwmapi = ctypes.WinDLL("dwmapi", use_last_error=True)
    user32.SetProcessDpiAwarenessContext.argtypes = [wintypes.HANDLE]
    user32.SetProcessDpiAwarenessContext.restype = wintypes.BOOL
    user32.GetForegroundWindow.restype = wintypes.HWND
    user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.GetWindowThreadProcessId.restype = wintypes.DWORD
    user32.IsWindowVisible.argtypes = [wintypes.HWND]
    user32.IsWindowVisible.restype = wintypes.BOOL
    user32.GetWindow.argtypes = [wintypes.HWND, wintypes.UINT]
    user32.GetWindow.restype = wintypes.HWND
    user32.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
    user32.GetWindowRect.restype = wintypes.BOOL
    user32.GetDpiForWindow.argtypes = [wintypes.HWND]
    user32.GetDpiForWindow.restype = wintypes.UINT
    user32.IsIconic.argtypes = [wintypes.HWND]
    user32.IsIconic.restype = wintypes.BOOL
    user32.IsZoomed.argtypes = [wintypes.HWND]
    user32.IsZoomed.restype = wintypes.BOOL
    user32.SetWindowPos.argtypes = [
        wintypes.HWND, wintypes.HWND, ctypes.c_int, ctypes.c_int,
        ctypes.c_int, ctypes.c_int, wintypes.UINT,
    ]
    user32.SetWindowPos.restype = wintypes.BOOL
    user32.MonitorFromWindow.argtypes = [wintypes.HWND, wintypes.DWORD]
    user32.MonitorFromWindow.restype = wintypes.HANDLE
    user32.GetMonitorInfoW.argtypes = [wintypes.HANDLE, ctypes.POINTER(MonitorInfo)]
    user32.GetMonitorInfoW.restype = wintypes.BOOL
    dwmapi.DwmGetWindowAttribute.argtypes = [
        wintypes.HWND, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD,
    ]
    dwmapi.DwmGetWindowAttribute.restype = ctypes.c_long

    if not user32.SetProcessDpiAwarenessContext(wintypes.HANDLE(-4)):
        raise ctypes.WinError(ctypes.get_last_error())
    window = find_window(user32, args.pid) if args.prepare else user32.GetForegroundWindow()
    if not window:
        raise RuntimeError("No foreground window is available.")
    window_pid = wintypes.DWORD()
    if not user32.GetWindowThreadProcessId(window, ctypes.byref(window_pid)):
        raise ctypes.WinError(ctypes.get_last_error())
    if window_pid.value != args.pid:
        raise RuntimeError("The isolated capture window is not in the foreground.")
    monitor = user32.MonitorFromWindow(window, 0)
    info = MonitorInfo(size=ctypes.sizeof(MonitorInfo))
    if not monitor or not user32.GetMonitorInfoW(monitor, ctypes.byref(info)):
        raise ctypes.WinError(ctypes.get_last_error())
    work = (info.work.left, info.work.top, info.work.right, info.work.bottom)

    def frame_bounds():
        frame = wintypes.RECT()
        result = dwmapi.DwmGetWindowAttribute(window, 9, ctypes.byref(frame), ctypes.sizeof(frame))
        if result:
            raise OSError(f"Could not read the native window frame (HRESULT {result:#x}).")
        return frame.left, frame.top, frame.right, frame.bottom

    bounds = frame_bounds()
    if args.prepare:
        dpi = user32.GetDpiForWindow(window)
        if not dpi:
            raise ctypes.WinError(ctypes.get_last_error())
        if abs(dpi / 96 - args.pixel_ratio) > 0.000001:
            raise RuntimeError(f"The capture window uses {dpi / 96:g}x display scaling, not {args.pixel_ratio:g}x.")
        if user32.IsIconic(window) or user32.IsZoomed(window):
            raise RuntimeError("Restore the isolated capture window before preparing it.")
        outer = wintypes.RECT()
        if not user32.GetWindowRect(window, ctypes.byref(outer)):
            raise ctypes.WinError(ctypes.get_last_error())
        placement = window_placement(
            (outer.left, outer.top, outer.right, outer.bottom), bounds, work, args.width, args.height)
        if not user32.SetWindowPos(window, None, *placement, 0x0214):
            raise ctypes.WinError(ctypes.get_last_error())
        bounds = frame_bounds()
        validate_window(args.pid, window_pid.value, bounds, work)
        if (bounds[2] - bounds[0], bounds[3] - bounds[1]) != (args.width, args.height):
            raise RuntimeError("Could not resize the native capture frame to the requested dimensions.")
        return

    validate_window(args.pid, window_pid.value, bounds, work)
    image = ImageGrab.grab(bbox=bounds, all_screens=True, include_layered_windows=True)
    if user32.GetForegroundWindow() != window:
        raise RuntimeError("The capture window lost focus during the screenshot.")
    image.save(args.output)


if __name__ == "__main__":
    main()
