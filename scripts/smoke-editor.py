"""Desktop integration checks for selection, annotations, editable storage and revisions.
Requires a running SnapRail, Python and Pillow. Sends real desktop input.
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
output = project / 'artifacts' / 'qa-v0.2'
output.mkdir(parents=True, exist_ok=True)
library = data_root()
images = library / 'images'
results = {}
ps = Path(os.environ['SystemRoot']) / 'System32' / 'WindowsPowerShell' / 'v1.0' / 'powershell.exe'


def uia(id=None, action='invoke', window='随手挂 · 选择截图区域', name=None, text=None):
    args = [str(ps), '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(project / 'scripts' / 'uia.ps1'), '-Window', window, '-Action', action]
    if id: args += ['-Id', id]
    if name: args += ['-Name', name]
    if text is not None: args += ['-Text', text]
    result = subprocess.run(args, capture_output=True, timeout=12)
    if result.returncode:
        raise AssertionError(result.stderr.decode('utf-8', errors='replace'))
    if result.stdout.strip(): return json.loads(result.stdout.decode('utf-8-sig'))


def key(k, up=False): u.keybd_event(k, 0, 2 if up else 0, 0)
def press(k): key(k); time.sleep(.035); key(k, True)
def shortcut(k, alt=False, shift=False):
    key(17)
    if alt: key(18)
    if shift: key(16)
    time.sleep(.04)
    press(ord(k) if isinstance(k, str) else k)
    if shift: key(16, True)
    if alt: key(18, True)
    key(17, True)
    time.sleep(.05)


def click(x, y, right=False):
    u.SetCursorPos(int(x), int(y)); time.sleep(.12)
    u.mouse_event(8 if right else 2, 0, 0, 0, 0); time.sleep(.04)
    u.mouse_event(16 if right else 4, 0, 0, 0, 0)


def drag(a, b):
    u.SetCursorPos(*a); u.mouse_event(2, 0, 0, 0, 0)
    for i in range(1, 16):
        u.SetCursorPos(round(a[0]+(b[0]-a[0])*i/15), round(a[1]+(b[1]-a[1])*i/15)); time.sleep(.02)
    u.mouse_event(4, 0, 0, 0, 0); time.sleep(.1)


def wait_file(before):
    for _ in range(100):
        fresh = set(images.glob('*.png')) - before
        if fresh:
            path = max(fresh, key=lambda p: p.stat().st_mtime)
            try:
                with Image.open(path) as image: image.load()
                return path
            except (PermissionError, OSError):
                pass
        time.sleep(.1)
    raise AssertionError('No new screenshot saved')


def document(path):
    return library / 'documents' / path.stem.split('-')[-1] / 'annotations.json'


target = subprocess.Popen([sys.executable, str(project / 'scripts' / 'qa-target.py')], creationflags=subprocess.CREATE_NO_WINDOW)
time.sleep(.8)

try:
    # Resizing an edge and moving by one physical pixel must preserve exact output coordinates.
    before = set(images.glob('*.png'))
    region = (450, 650, 950, 950)
    baseline = ImageGrab.grab().crop((451, 650, 1051, 950)).convert('RGB')
    shortcut('A', alt=True); time.sleep(.7)
    drag(region[:2], region[2:])
    drag((950, 800), (1050, 800))
    press(0x27)  # one actual pixel right
    time.sleep(.2)
    ImageGrab.grab().resize((1536, 960)).save(output / 'resize-magnifier.png')
    press(13)
    resized = wait_file(before)
    actual = Image.open(resized).convert('RGB')
    assert actual.size == (600, 300), actual.size
    assert ImageChops.difference(actual, baseline).getbbox() is None, 'Resize/move coordinates differ'
    results['resize_edge_and_one_physical_pixel_move'] = True

    before = set(images.glob('*.png'))
    shortcut('A', alt=True); time.sleep(.7)
    drag((450, 650), (1450, 1150))
    for tool, a, b in [
        ('rectangle', (500, 700), (780, 900)),
        ('ellipse', (850, 700), (1080, 880)),
        ('arrow', (550, 1000), (900, 1000)),
        ('pen', (600, 1050), (850, 1100)),
        ('mosaic', (1150, 750), (1400, 950)),
    ]:
        uia('tool-' + tool); drag(a, b)
    shortcut('Z'); time.sleep(.1)
    assert uia('redo', 'bounds')['Enabled'], 'Undo did not create redo state'
    shortcut('Y')
    assert not uia('redo', 'bounds')['Enabled'], 'Redo did not consume redo state'
    results['undo_redo_desktop_shortcuts'] = True
    uia('tool-text'); click(950, 990); time.sleep(.1)
    uia('annotation-text', 'value', text='随手挂\nSnapRail')
    shortcut(0x23)  # UIA SetValue leaves the caret at the beginning; move to end first.
    # Enter must add a newline instead of prematurely completing the screenshot.
    press(13); time.sleep(.1)
    assert set(images.glob('*.png')) == before, 'Enter inside text prematurely confirmed screenshot'
    shortcut(13)
    assert not any(item['Id'] == 'annotation-text' for item in uia(action='list')), 'Ctrl+Enter did not commit text'
    ImageGrab.grab().resize((1536, 960)).save(output / 'annotated-selection.png')
    uia('confirm')
    annotated = wait_file(before)
    doc = json.loads(document(annotated).read_text(encoding='utf-8-sig'))
    assert [m['Tool'] for m in doc['Marks']] == ['Rectangle', 'Ellipse', 'Arrow', 'Pen', 'Mosaic', 'Text'], doc
    assert doc['Marks'][-1]['Text'].startswith('随手挂\nSnapRail'), doc['Marks'][-1]
    assert doc['Width'] == 1000 and doc['Height'] == 500
    original_path = document(annotated).with_name('original.png')
    original = Image.open(original_path).convert('RGB')
    flattened = Image.open(annotated).convert('RGB')
    assert ImageChops.difference(original, flattened).getbbox() is not None
    results['six_annotation_tools_and_chinese_multiline_text'] = True
    results['editable_original_and_metadata_saved'] = True
    old_hash = hashlib.sha256(annotated.read_bytes()).hexdigest()

    # Open the latest capture via a real right click, then edit its unflattened source.
    # Reveal using the top edge so auto-hide cannot race the right click.
    u.SetCursorPos(u.GetSystemMetrics(0)//2, 0); time.sleep(.6)
    box = uia('image-' + annotated.stem.split('-')[-1], 'bounds', window='SnapRail · 随手挂')
    click(box['X'] + box['Width']/2, box['Y'] + box['Height']/2, right=True)
    time.sleep(.2)
    ImageGrab.grab().resize((1536, 960)).save(output / 'reopen-context.png')
    uia(window='SnapRail · 随手挂', name='编辑副本…')
    time.sleep(.4)
    controls = uia(action='list', window='随手挂 · 编辑截图')
    assert any(item['Id'] == 'confirm' for item in controls)
    before = set(images.glob('*.png'))
    uia('undo', window='随手挂 · 编辑截图')
    uia('confirm', window='随手挂 · 编辑截图')
    revision = wait_file(before)
    revised_doc = json.loads(document(revision).read_text(encoding='utf-8-sig'))
    assert len(revised_doc['Marks']) == 5, revised_doc
    assert hashlib.sha256(annotated.read_bytes()).hexdigest() == old_hash, 'Editing overwrote old version'
    results['reopen_edit_undo_save_new_version'] = True
    results['old_version_preserved'] = True

    # Full-screen selection creates a full native-resolution output, with no UI overlay baked in.
    before = set(images.glob('*.png'))
    shortcut('A', alt=True); time.sleep(.7); shortcut('A'); time.sleep(.2); press(13)
    full = wait_file(before)
    assert Image.open(full).size == (u.GetSystemMetrics(0), u.GetSystemMetrics(1))
    results['full_screen_selection'] = True
    before = set(images.glob('*.png'))
    shortcut('A', alt=True); time.sleep(.6)
    key(18); time.sleep(.04); press(0x73); key(18, True); time.sleep(.4)
    shortcut('A', alt=True); time.sleep(.6)
    assert any('Ctrl+A' in item['Name'] for item in uia(action='list')), 'Capture stuck after Alt+F4'
    press(27); time.sleep(.2)
    assert set(images.glob('*.png')) == before
    results['alt_f4_cancel_allows_next_capture'] = True
finally:
    target.terminate()
    target.wait(timeout=5)
    (output / 'editor-results.json').write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(results, ensure_ascii=False, indent=2))
