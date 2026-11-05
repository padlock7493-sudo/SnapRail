"""Real desktop smoke test. Requires Python + Pillow and a running SnapRail.
Moves the mouse and sends keys; run while the desktop is idle.
Only creates new screenshots; does not remove existing captures.
"""
from qa_paths import data_root, settings_root, launch_args

import ctypes
import json
import os
from pathlib import Path
import time
import tkinter as tk
from PIL import Image, ImageChops, ImageGrab

user32 = ctypes.windll.user32
user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
root_dir = Path(__file__).resolve().parent.parent
output = root_dir / 'artifacts' / 'qa'
output.mkdir(parents=True, exist_ok=True)
images = data_root()/'images'
results = {}


def key(code, up=False):
    user32.keybd_event(code, 0, 2 if up else 0, 0)


def hotkey(letter):
    key(0x11)
    key(0x12)
    key(ord(letter))
    key(ord(letter), True)
    key(0x12, True)
    key(0x11, True)


def press(code):
    key(code)
    key(code, True)


def windows():
    found = []
    callback = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)

    @callback
    def visit(hwnd, _):
        text = ctypes.create_unicode_buffer(256)
        user32.GetWindowTextW(ctypes.c_void_p(hwnd), text, 256)
        if user32.IsWindowVisible(ctypes.c_void_p(hwnd)):
            found.append(text.value)
        return True

    user32.EnumWindows(visit, 0)
    return found


def wait_for(predicate, timeout=6):
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        if predicate():
            return
        time.sleep(.08)
    raise AssertionError('Timed out waiting for desktop state')


def drag(x1, y1, x2, y2):
    user32.SetCursorPos(x1, y1)
    user32.mouse_event(2, 0, 0, 0, 0)
    for index in range(1, 16):
        user32.SetCursorPos(int(x1 + (x2-x1)*index/15), int(y1 + (y2-y1)*index/15))
        time.sleep(.015)
    user32.mouse_event(4, 0, 0, 0, 0)


def read_image(path):
    for attempt in range(40):
        try:
            with Image.open(path) as image:
                return image.convert('RGB')
        except (PermissionError, OSError):
            if attempt == 39: raise
            time.sleep(.05)


target = tk.Tk()
target.title('SnapRail QA target')
target.geometry('900x480+350+520')
target.configure(bg='#126b64')
target.attributes('-topmost', True)
canvas = tk.Canvas(target, width=900, height=480, bg='#126b64', highlightthickness=0)
canvas.pack(fill='both', expand=True)
canvas.create_rectangle(60, 60, 290, 260, fill='#e9b56d', outline='')
canvas.create_rectangle(330, 60, 700, 320, fill='#e6f2ee', outline='')
canvas.create_text(475, 140, text='SnapRail', fill='#253a40', font=('Segoe UI', 30, 'bold'))
canvas.create_text(475, 205, text='Capture. Hang. Copy.', fill='#126b64', font=('Segoe UI', 18))
target.update()
time.sleep(.3)
# Let the capture overlay become topmost over this target.
target.attributes('-topmost', False)
target.update()

try:
    before = set(images.glob('*.png'))
    sequence = user32.GetClipboardSequenceNumber()
    hotkey('A')
    wait_for(lambda: any('选择截图区域' in text for text in windows()))
    time.sleep(.7)
    press(0x1B)
    wait_for(lambda: not any('选择截图区域' in text for text in windows()))
    assert set(images.glob('*.png')) == before
    assert user32.GetClipboardSequenceNumber() == sequence
    results['cancel_preserves_files_and_clipboard'] = True

    # Capture a known target region and compare every output pixel with the live baseline.
    region = (450, 650, 950, 950)
    baseline = ImageGrab.grab().crop(region).convert('RGB')
    hotkey('A')
    wait_for(lambda: any('选择截图区域' in text for text in windows()))
    time.sleep(.3)
    drag(*region)
    time.sleep(.2)
    ImageGrab.grab().resize((1536, 960)).save(output / 'selection.png')
    press(0x0D)
    wait_for(lambda: bool(set(images.glob('*.png')) - before))
    path = next(iter(set(images.glob('*.png')) - before))
    actual = read_image(path)
    assert actual.size == (500, 300), actual.size
    difference = ImageChops.difference(actual, baseline)
    assert difference.getbbox() is None, 'Captured pixels differ from desktop baseline'
    wait_for(lambda: user32.IsClipboardFormatAvailable(8) != 0)
    results['capture_pixel_exact_500x300'] = True
    results['clipboard_bitmap_available'] = True
    wait_for(lambda: 'SnapRail · 随手挂' in windows())
    time.sleep(.8)
    ImageGrab.grab().crop((700, 0, 2350, 450)).save(output / 'rail.png')
    results['auto_rail_visible'] = True
    hotkey('T')
    wait_for(lambda: 'SnapRail · 随手挂' not in windows())
    results['toggle_hides'] = True
    hotkey('T')
    wait_for(lambda: 'SnapRail · 随手挂' in windows())
    results['toggle_shows'] = True
    user32.SetCursorPos(1500, 1100)
    wait_for(lambda: 'SnapRail · 随手挂' not in windows(), 8)
    results['auto_hide'] = True
    user32.SetCursorPos(user32.GetSystemMetrics(0)//2, 0)
    wait_for(lambda: 'SnapRail · 随手挂' in windows())
    results['top_edge_reveals'] = True
    user32.SetCursorPos(1500, 1100)
    results['capture_file'] = str(path)

    # Put known content behind the rail. Capturing there must exclude the rail itself.
    target.geometry('900x480+1050+0')
    target.update()
    hotkey('T')
    wait_for(lambda: 'SnapRail · 随手挂' not in windows())
    time.sleep(.3)
    region2 = (1200, 90, 1600, 330)
    baseline2 = ImageGrab.grab().crop(region2).convert('RGB')
    hotkey('T')
    wait_for(lambda: 'SnapRail · 随手挂' in windows())
    before2 = set(images.glob('*.png'))
    hotkey('A')
    wait_for(lambda: any('选择截图区域' in text for text in windows()))
    time.sleep(.5)
    drag(*region2)
    time.sleep(.2)
    press(0x0D)
    wait_for(lambda: bool(set(images.glob('*.png')) - before2))
    path2 = next(iter(set(images.glob('*.png')) - before2))
    actual2 = read_image(path2)
    assert actual2.size == (400, 240), actual2.size
    assert ImageChops.difference(actual2, baseline2).getbbox() is None, 'Rail contaminated screenshot'
    results['capture_excludes_own_rail_pixel_exact'] = True
finally:
    target.destroy()
    (output / 'smoke-results.json').write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(results, ensure_ascii=False, indent=2))
