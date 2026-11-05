"""M3 desktop checks: pin, zoom, favorites, drag/drop, cancellation and restart.
Requires a running SnapRail and the built behavioral-test executable.
Creates local test images only; does not send to chats or upload anywhere.
"""
from qa_paths import data_root, settings_root, launch_args

import ctypes
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time
from PIL import Image, ImageChops, ImageGrab

u = ctypes.windll.user32
u.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
project = Path(__file__).resolve().parent.parent
output = project / 'artifacts' / 'qa-v0.3'
output.mkdir(parents=True, exist_ok=True)
library = data_root()
images = library / 'images'
ps = Path(os.environ['SystemRoot']) / 'System32' / 'WindowsPowerShell' / 'v1.0' / 'powershell.exe'
app_exe = Path(os.environ.get('SNAPRAIL_TEST_EXE', str(project / 'src' / 'SnapRail.App' / 'bin' / 'Release' / 'net10.0-windows' / 'SnapRail.exe')))
test_env = os.environ.copy()
test_env['DOTNET_ROOT'] = str(Path.home() / '.dotnet')
results = {}


def wait(predicate, timeout=8):
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        if predicate(): return
        time.sleep(.1)
    raise AssertionError('Desktop state did not become ready')


def uia(id=None, action='invoke', window='SnapRail · 随手挂', name=None):
    args = [str(ps), '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(project/'scripts'/'uia.ps1'), '-Window', window, '-Action', action]
    if id: args += ['-Id', id]
    if name: args += ['-Name', name]
    result = subprocess.run(args, capture_output=True, timeout=12, creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode: raise AssertionError(result.stderr.decode('utf-8', errors='replace'))
    return json.loads(result.stdout.decode('utf-8-sig')) if result.stdout.strip() else None


def key(code, up=False): u.keybd_event(code, 0, 2 if up else 0, 0)
def press(code): key(code); time.sleep(.04); key(code, True)
def hotkey(letter):
    key(17); key(18); time.sleep(.04); press(ord(letter)); key(18, True); key(17, True); time.sleep(.1)


def click(x, y, right=False):
    u.SetCursorPos(round(x), round(y)); time.sleep(.15)
    u.mouse_event(8 if right else 2, 0, 0, 0, 0); time.sleep(.05)
    u.mouse_event(16 if right else 4, 0, 0, 0, 0)


def drag(start, end, cancel=False, hold=False):
    u.SetCursorPos(round(start[0]), round(start[1])); time.sleep(.1)
    u.mouse_event(2, 0, 0, 0, 0); time.sleep(.06)
    u.SetCursorPos(round(start[0]+12), round(start[1]+12))
    if hold: time.sleep(.5)  # allow asynchronous PNG preparation before OLE enters drag mode
    for step in range(1, 21):
        u.SetCursorPos(round(start[0]+(end[0]-start[0])*step/20), round(start[1]+(end[1]-start[1])*step/20)); time.sleep(.025)
    if cancel: press(27); time.sleep(.1)
    u.mouse_event(4, 0, 0, 0, 0); time.sleep(.2)


def read_png(path):
    for attempt in range(50):
        try:
            with Image.open(path) as image: return image.convert('RGB')
        except OSError:
            if attempt == 49: raise
            time.sleep(.05)


def new_file(before):
    wait(lambda: bool(set(images.glob('*.png')) - before))
    path = max(set(images.glob('*.png')) - before, key=lambda p: p.stat().st_mtime)
    read_png(path)
    return path


def state(id):
    try: return json.loads((library/'library.json').read_text(encoding='utf-8-sig'))['Items'].get(id, {})
    except (OSError, json.JSONDecodeError): return {}


def reveal():
    u.SetCursorPos(u.GetSystemMetrics(0)//2, 0); time.sleep(.7)


def context(id, name):
    reveal()
    box = uia('image-'+id, 'bounds')
    click(box['X']+box['Width']/2, box['Y']+box['Height']/2, True)
    time.sleep(.15); uia(name=name)


target = subprocess.Popen([sys.executable, str(project/'scripts'/'qa-target.py')], creationflags=subprocess.CREATE_NO_WINDOW)
receiver = None
try:
    time.sleep(.8)
    baseline = ImageGrab.grab().crop((450, 650, 950, 950)).convert('RGB')
    before = set(images.glob('*.png'))
    hotkey('A'); time.sleep(.6); drag((450,650), (950,950)); press(13)
    original = new_file(before); id = original.stem.split('-')[-1]
    original_hash = hashlib.sha256(original.read_bytes()).hexdigest()
    pin_title = '随手挂 · 桌面贴图 ' + id
    context(id, '固定收藏')
    wait(lambda: state(id).get('Favorite') is True)
    results['favorite_persisted'] = True

    reveal(); uia('pin-'+id)
    wait(lambda: state(id).get('IsPinned') is True)
    box = uia(action='window', window=pin_title)
    assert u.GetWindowLongW(ctypes.c_void_p(box['Handle']), -20) & 0x8, 'Pin is not topmost'
    initial_box = dict(box)
    initial_width = box['Width']
    drag((box['X']+25, box['Y']+24), (545,724))
    u.SetCursorPos(650,850); u.mouse_event(0x0800,0,0,120,0); time.sleep(.7)
    box = uia(action='window', window=pin_title)
    assert box['Width'] > initial_width, box
    results['clip_opens_topmost_pin_and_wheel_zooms'] = True
    saved_placement = state(id)['Placement']
    assert abs(box['X'] - initial_box['X']) > 50, (initial_box, box)
    assert abs(saved_placement['X'] - box['X']) <= 2 and abs(saved_placement['Width'] - box['Width']) <= 2, (saved_placement, box)
    results['pin_drag_position_saved'] = True
    sequence = u.GetClipboardSequenceNumber()
    uia('pin-copy', window=pin_title)
    wait(lambda: u.GetClipboardSequenceNumber() != sequence)
    results['pin_copy'] = True
    ImageGrab.grab().resize((1536,960)).save(output/'pin.png')

    # Re-capture the same known desktop region while a pin overlaps it.
    before = set(images.glob('*.png'))
    hotkey('A'); time.sleep(.6); drag((450,650), (950,950)); press(13)
    clean = new_file(before)
    assert ImageChops.difference(read_png(clean), baseline).getbbox() is None, 'Pin leaked into screenshot'
    uia(action='window', window=pin_title)
    results['capture_hides_and_restores_pin_pixel_exact'] = True
    uia('pin-return', window=pin_title)
    wait(lambda: state(id).get('IsPinned') is False)
    reveal(); uia('image-'+id, 'bounds')
    results['pin_returns_to_rail'] = True

    drop_folder = output / ('drop-' + str(time.time_ns()))
    test_dll = project/'tests'/'SnapRail.Tests'/'bin'/'Release'/'net10.0-windows'/'SnapRail.Tests.dll'
    receiver = subprocess.Popen([str(Path.home()/'.dotnet'/'dotnet.exe'), str(test_dll), '--drop-target', str(drop_folder)], env=test_env, creationflags=subprocess.CREATE_NO_WINDOW)
    wait(lambda: (drop_folder/'ready.json').exists())
    destination = json.loads((drop_folder/'ready.json').read_text())
    end = (destination['X']+destination['Width']/2, destination['Y']+destination['Height']/2)
    reveal(); box = uia('image-'+id, 'bounds')
    start = (box['X']+box['Width']/2, box['Y']+box['Height']/2)
    sequence = u.GetClipboardSequenceNumber()
    drag(start, end, hold=True)
    wait(lambda: (drop_folder/'drop.json').exists())
    dropped = json.loads((drop_folder/'drop.json').read_text())
    assert dropped['HasBitmap'] and dropped['Effects'] == 'Copy', dropped
    exported = Path(dropped['Files'][0])
    assert exported != original and exported.parent.name == 'exports'
    assert (drop_folder/exported.name).exists()
    assert hashlib.sha256(original.read_bytes()).hexdigest() == original_hash
    assert u.GetClipboardSequenceNumber() == sequence, 'Drag incorrectly triggered copy click'
    results['real_ole_drag_copies_png_and_bitmap'] = True
    results['drag_keeps_original_and_clipboard_unchanged'] = True
    old_drop = (drop_folder/'drop.json').stat().st_mtime_ns
    reveal(); box = uia('image-'+id, 'bounds')
    drag((box['X']+box['Width']/2, box['Y']+box['Height']/2), end, cancel=True, hold=True)
    assert (drop_folder/'drop.json').stat().st_mtime_ns == old_drop
    assert hashlib.sha256(original.read_bytes()).hexdigest() == original_hash
    results['escape_cancels_drag_without_file_loss'] = True

    # Graceful exit must preserve an open pin, and launching again must restore it.
    reveal(); uia('pin-'+id)
    wait(lambda: state(id).get('IsPinned') is True)
    saved_box = uia(action='window', window=pin_title)
    subprocess.run([str(app_exe), '--exit'], env=test_env, timeout=8, creationflags=subprocess.CREATE_NO_WINDOW, check=True)
    time.sleep(1)
    subprocess.Popen(launch_args(app_exe), env=test_env, creationflags=subprocess.CREATE_NO_WINDOW)
    time.sleep(1.3)
    restored = uia(action='window', window=pin_title)
    assert abs(restored['X'] - saved_box['X']) <= 2 and abs(restored['Width'] - saved_box['Width']) <= 2, (saved_box, restored)
    assert state(id)['Favorite'] and state(id)['IsPinned']
    results['restart_restores_favorite_and_pin_geometry'] = True
    uia('pin-return', window=pin_title)
    context(id, '取消固定收藏')
    wait(lambda: state(id).get('Favorite') is False and state(id).get('IsPinned') is False)
    results['test_favorite_and_pin_cleaned_up'] = True
finally:
    target.terminate(); target.wait(timeout=5)
    if receiver: receiver.terminate(); receiver.wait(timeout=5)
    (output/'rail-results.json').write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(results, ensure_ascii=False, indent=2))
