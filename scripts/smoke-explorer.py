"""Real Explorer cross-volume drag check. Creates one local QA folder and closes its window."""
from qa_paths import data_root, settings_root, launch_args

import ctypes
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time
from PIL import ImageGrab

u = ctypes.windll.user32
u.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
project = Path(__file__).resolve().parent.parent
output = project / 'artifacts' / 'qa-v0.3'
ps = Path(os.environ['SystemRoot']) / 'System32' / 'WindowsPowerShell' / 'v1.0' / 'powershell.exe'
exports = Path(os.environ['TEMP']) / 'SnapRail' / 'exports'
exported = max(exports.glob('SnapRail-*.png'), key=lambda p: p.stat().st_mtime)
id = exported.stem.split('-')[-1]
original = next((data_root()/'images').glob('*-'+id+'.png'))
original_hash = hashlib.sha256(original.read_bytes()).hexdigest()
folder = output / ('Explorer-Drop-' + str(time.time_ns()))


def uia(action='bounds', id=None):
    args = [str(ps), '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(project/'scripts'/'uia.ps1'), '-Window', 'SnapRail · 随手挂', '-Action', action]
    if id: args += ['-Id', id]
    result = subprocess.run(args, capture_output=True, timeout=12, creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode: raise AssertionError(result.stderr.decode('utf-8', errors='replace'))
    return json.loads(result.stdout.decode('utf-8-sig')) if result.stdout.strip() else None


result = subprocess.run([str(ps), '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(project/'scripts'/'explorer-target.ps1'), '-Folder', str(folder)], capture_output=True, timeout=15, creationflags=subprocess.CREATE_NO_WINDOW)
if result.returncode: raise AssertionError(result.stderr.decode('utf-8', errors='replace'))
target = json.loads(result.stdout.decode('utf-8-sig'))
try:
    time.sleep(1.2)
    u.SetCursorPos(u.GetSystemMetrics(0)//2, 0); time.sleep(.7)
    box = uia(id='image-'+id)
    start = (box['X']+box['Width']/2, box['Y']+box['Height']/2)
    end = (target['X']+650, target['Y']+500)
    sequence = u.GetClipboardSequenceNumber()
    ImageGrab.grab().resize((1536,960)).save(output/'explorer-before.png')
    u.SetCursorPos(round(start[0]),round(start[1])); time.sleep(.1); u.mouse_event(2,0,0,0,0)
    u.SetCursorPos(round(start[0]+15),round(start[1]+15)); time.sleep(.5)
    for step in range(1,26):
        u.SetCursorPos(round(start[0]+(end[0]-start[0])*step/25),round(start[1]+(end[1]-start[1])*step/25)); time.sleep(.025)
    u.mouse_event(4,0,0,0,0)
    time.sleep(.25)
    ImageGrab.grab().resize((1536,960)).save(output/'explorer-after.png')
    for _ in range(100):
        if (folder/exported.name).exists(): break
        time.sleep(.1)
    received = folder/exported.name
    assert received.exists(), 'Explorer did not receive PNG'
    assert hashlib.sha256(received.read_bytes()).hexdigest() == hashlib.sha256(exported.read_bytes()).hexdigest()
    assert hashlib.sha256(original.read_bytes()).hexdigest() == original_hash
    assert u.GetClipboardSequenceNumber() == sequence, 'Drag triggered a copy click'
    # Keyboard/accessibility activation must still copy after the completed drag.
    u.SetCursorPos(u.GetSystemMetrics(0)//2,0); time.sleep(.6)
    uia('invoke', 'image-'+id); time.sleep(.2)
    assert u.GetClipboardSequenceNumber() != sequence, 'Copy activation remained suppressed after drag'
    (output/'explorer-results.json').write_text(json.dumps({'real_explorer_cross_drive_copy': True, 'original_unchanged': True, 'drag_does_not_copy_to_clipboard': True, 'copy_works_after_drag': True, 'received': str(received)}, ensure_ascii=False, indent=2), encoding='utf-8')
    print('PASS Explorer cross-drive copy, original preservation, clipboard and post-drag copy')
finally:
    u.PostMessageW(ctypes.c_void_p(target['Handle']), 0x0010, 0, 0)
