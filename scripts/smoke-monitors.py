"""Pixel-exact tests on actual mixed-DPI displays, including a spanning selection."""
from qa_paths import data_root, settings_root, launch_args

import ctypes
import json
import os
from pathlib import Path
import subprocess
import sys
import time
from PIL import Image, ImageGrab, ImageChops
from monitors import monitors

u = ctypes.windll.user32
u.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
project = Path(__file__).resolve().parent.parent
output = project/'artifacts'/'qa-v0.4'; output.mkdir(parents=True,exist_ok=True)
layout = monitors(); assert len(layout) >= 2
left=min(m['bounds'][0] for m in layout); top=min(m['bounds'][1] for m in layout)
images = data_root()/'images'
ps = Path(os.environ['SystemRoot'])/'System32'/'WindowsPowerShell'/'v1.0'/'powershell.exe'
ready=output/('targets-'+str(time.time_ns())+'.json')
target=subprocess.Popen([sys.executable,str(project/'scripts'/'qa-monitors.py'),str(ready)],creationflags=subprocess.CREATE_NO_WINDOW)

def key(k,up=False): u.keybd_event(k,0,2 if up else 0,0)
def press(k): key(k);time.sleep(.04);key(k,True)
def hotkey():
    key(17);key(18);time.sleep(.05);press(65);key(18,True);key(17,True)
def drag(a,b):
    u.SetCursorPos(*a);time.sleep(.08);u.mouse_event(2,0,0,0,0)
    for i in range(1,25):
        u.SetCursorPos(round(a[0]+(b[0]-a[0])*i/24),round(a[1]+(b[1]-a[1])*i/24));time.sleep(.018)
    u.mouse_event(4,0,0,0,0);time.sleep(.15)
def uia(action='window',window='SnapRail · 随手挂',id=None):
    args=[str(ps),'-NoProfile','-ExecutionPolicy','Bypass','-File',str(project/'scripts'/'uia.ps1'),'-Window',window,'-Action',action]
    if id:args+=['-Id',id]
    r=subprocess.run(args,capture_output=True,timeout=12,creationflags=subprocess.CREATE_NO_WINDOW)
    assert r.returncode==0,r.stderr
    return json.loads(r.stdout.decode('utf-8-sig')) if r.stdout.strip() else None
def capture(region):
    before=set(images.glob('*.png'))
    # The expanded rail can overlap y=350 on high-DPI screens; hide it before
    # sampling the baseline, just as the app does for the actual screenshot.
    try: uia(action='close')
    except AssertionError: pass  # The rail may already be hidden.
    time.sleep(.25)
    u.SetCursorPos(region[0]+10,region[1]+10);time.sleep(.2)
    baseline=ImageGrab.grab(all_screens=True).crop((region[0]-left,region[1]-top,region[2]-left,region[3]-top)).convert('RGB')
    baseline.save(output/'capture-baseline.png')
    hotkey();time.sleep(1.0)
    overlay=uia(window='随手挂 · 选择截图区域')
    assert overlay['X']==left and overlay['Y']==top,(overlay,left,top)
    drag(region[:2],region[2:]);time.sleep(.15)
    ImageGrab.grab(all_screens=True).resize((1600,612)).save(output/'selection-debug.png')
    press(13)
    for _ in range(100):
        new=set(images.glob('*.png'))-before
        if new:break
        time.sleep(.08)
    assert new,'No capture'
    path=next(iter(new))
    for _ in range(40):
        try:
            with Image.open(path) as im: actual=im.convert('RGB')
            break
        except OSError:time.sleep(.05)
    assert actual.size==(region[2]-region[0],region[3]-region[1]),actual.size
    actual.save(output/'capture-actual.png')
    assert ImageChops.difference(actual,baseline).getbbox() is None,region
    return path

results={'monitors':layout}
try:
    for _ in range(50):
        if ready.exists():break
        time.sleep(.1)
    time.sleep(.4)
    for index,m in enumerate(layout):
        x,y,_,_=m['bounds']
        capture((x+350,y+350,x+750,y+650))
        rail=uia(); wx,wy,wr,wb=m['work']
        assert rail['X']>=wx and rail['X']+rail['Width']<=wr+2 and rail['Y']>=wy,(m,rail)
        results[f'monitor_{index+1}_pixel_exact_and_rail_position']=True
    # Spanning the actual shared edge at x=0 on this setup.
    if any(m['bounds'][0]<0 for m in layout):
        path=capture((-200,500,200,800))
        results['cross_monitor_pixel_exact_400x300']=True
    for m in layout:
        x,y,r,b=m['bounds'];u.SetCursorPos((x+r)//2,y);time.sleep(.7)
        rail=uia(); assert x<=rail['X']<r,(m,rail)
    results['hover_moves_rail_between_displays']=True
    id=path.stem.split('-')[-1]
    uia(action='invoke',id='pin-'+id);time.sleep(.5)
    pin_title='随手挂 · 桌面贴图 '+id
    pin=uia(window=pin_title)
    secondary=next(m for m in layout if not m['primary'])
    sx,sy,sr,sb=secondary['work']
    drag((round(pin['X']+25),round(pin['Y']+25)),(sx+400,sy+350));time.sleep(.9)
    moved=uia(window=pin_title)
    assert sx<=moved['X'] and moved['X']+moved['Width']<=sr+2 and sy<=moved['Y']<sb,(secondary,moved)
    results['pin_drag_between_mixed_dpi_monitors']=True
    app=Path(os.environ.get('SNAPRAIL_TEST_EXE',str(project/'src'/'SnapRail.App'/'bin'/'Release'/'net10.0-windows'/'SnapRail.exe')))
    env=os.environ.copy();env['DOTNET_ROOT']=str(Path.home()/'.dotnet')
    subprocess.run([str(app),'--exit'],env=env,timeout=10,creationflags=subprocess.CREATE_NO_WINDOW);time.sleep(.8)
    subprocess.Popen(launch_args(app),env=env,creationflags=subprocess.CREATE_NO_WINDOW);time.sleep(1.3)
    restored=uia(window=pin_title)
    assert abs(restored['X']-moved['X'])<=2 and abs(restored['Width']-moved['Width'])<=2,(moved,restored)
    results['secondary_pin_geometry_restored_after_restart']=True
    uia(action='invoke',window=pin_title,id='pin-return')
    ImageGrab.grab(all_screens=True).resize((1600,612)).save(output/'mixed-dpi-desktop.png')
finally:
    target.terminate();target.wait(timeout=5)
    (output/'monitors-results.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(results,ensure_ascii=False,indent=2))
