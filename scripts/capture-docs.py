"""Capture real UI screenshots against a synthetic desktop, using an isolated portable folder.

Requires Python + Pillow on an idle Windows desktop and a published SnapRail.exe.
The background contains only locally authored demonstration content.
"""
import ctypes
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

u = ctypes.windll.user32
u.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
project = Path(__file__).resolve().parent.parent

if '--background' in sys.argv:
    import tkinter as tk
    app = tk.Tk()
    app.title('SnapRail documentation background')
    app.overrideredirect(True)
    app.geometry('3072x1920+0+0')
    app.attributes('-topmost', True)
    c = tk.Canvas(app, background='#EAF1EF', highlightthickness=0)
    c.pack(fill='both', expand=True)
    c.create_text(350, 560, anchor='w', text='随手挂 · SnapRail 1.0.0', fill='#245C57', font=('Microsoft YaHei UI', 38, 'bold'))
    c.create_text(350, 650, anchor='w', text='截一张，随手挂一下！', fill='#536F68', font=('Microsoft YaHei UI', 23))
    for x, title, accent in [(350, '灵感清单', '#80BCA3'), (1170, '配色备忘', '#E8AE78'), (1990, '项目笔记', '#83BAD3')]:
        c.create_rectangle(x, 780, x+730, 1390, fill='#FFFFFF', outline='#CDDCD6', width=2)
        c.create_rectangle(x, 780, x+730, 795, fill=accent, outline='')
        c.create_text(x+55, 880, text=title, anchor='w', fill='#294B47', font=('Microsoft YaHei UI', 24, 'bold'))
    for i, text in enumerate(['记录一个好点子', '截图标注重点', '随时找回灵感']):
        c.create_oval(410, 975+i*105, 430, 995+i*105, fill='#80BCA3', outline='')
        c.create_text(465, 985+i*105, text=text, anchor='w', fill='#45625B', font=('Microsoft YaHei UI', 21))
    for i, color in enumerate(['#83BAD3','#8BC6AD','#F1BA89','#C0ACD2']):
        x=1220+i*160
        c.create_rectangle(x, 975, x+125, 1140, fill=color, outline='')
        c.create_text(x+62, 1200, text=color, fill='#536F68', font=('Segoe UI', 15))
    c.create_text(2045, 1000, text='Windows x64', anchor='w', fill='#345D70', font=('Segoe UI', 25, 'bold'))
    c.create_text(2045, 1100, text='免安装 · 本地保存', anchor='w', fill='#45625B', font=('Microsoft YaHei UI', 20))
    c.create_text(2045, 1200, text='截图 / 标注 / 挂图', anchor='w', fill='#45625B', font=('Microsoft YaHei UI', 20))
    c.create_text(350, 1550, anchor='w', text='演示内容 · 本地生成，无个人资料', fill='#7F9690', font=('Microsoft YaHei UI', 17))
    app.mainloop()
    sys.exit(0)

from PIL import ImageGrab, Image
publish = project/'artifacts/SnapRail-win-x64/SnapRail.exe'
session = project/('artifacts/doc-session-'+str(time.time_ns()))/'SnapRail-Portable'
assert not session.exists(), 'Use a fresh documentation session'
session.mkdir(parents=True)
shutil.copy2(publish, session/'SnapRail.exe')
(session/'SnapRail.portable').write_text('Portable documentation session')
(session/'settings.json').write_text(json.dumps({'Version':1,'DataRoot':'.','HoverEnabled':False,'AnimationEnabled':False,'RailScreen':r'\\.\DISPLAY1'}))
output = project/'docs/images'
output.mkdir(parents=True,exist_ok=True)
exe = session/'SnapRail.exe'
ps = Path(os.environ['SystemRoot'])/'System32/WindowsPowerShell/v1.0/powershell.exe'

def ui(id=None, action='invoke', window='SnapRail · 随手挂', text=None):
    args=[str(ps),'-NoProfile','-File',str(project/'scripts/uia.ps1'),'-Window',window,'-Action',action]
    if id: args+=['-Id',id]
    if text is not None: args+=['-Text',text]
    r=subprocess.run(args,capture_output=True,timeout=15,creationflags=subprocess.CREATE_NO_WINDOW)
    assert r.returncode==0,r.stderr.decode('utf-8',errors='replace')
    return json.loads(r.stdout.decode('utf-8-sig')) if r.stdout.strip() else None

def press(k):
    u.keybd_event(k,0,0,0);time.sleep(.04);u.keybd_event(k,0,2,0)

def hotkey(k):
    u.keybd_event(17,0,0,0);u.keybd_event(18,0,0,0);press(k)
    u.keybd_event(18,0,2,0);u.keybd_event(17,0,2,0);time.sleep(.8)

def drag(a,b):
    u.SetCursorPos(*a);u.mouse_event(2,0,0,0,0)
    for i in range(1,26):
        u.SetCursorPos(round(a[0]+(b[0]-a[0])*i/25),round(a[1]+(b[1]-a[1])*i/25));time.sleep(.015)
    u.mouse_event(4,0,0,0,0);time.sleep(.2)

def show():
    u.SetCursorPos(1500,600)
    subprocess.run([str(exe)],timeout=12,creationflags=subprocess.CREATE_NO_WINDOW)
    time.sleep(.5)

def screenshot(name, box):
    u.SetCursorPos(3000,1800);time.sleep(.2)
    image=ImageGrab.grab(bbox=tuple(round(v) for v in box))
    if image.width>1600:
        image=image.resize((1600,round(image.height*1600/image.width)),Image.Resampling.LANCZOS)
    image.save(output/(name+'.png'),optimize=True)

def windowshot(name,title):
    b=ui(action='window',window=title)
    u.SetWindowPos(ctypes.c_void_p(b['Handle']),ctypes.c_void_p(-1),0,0,0,0,0x43)
    u.SetForegroundWindow(ctypes.c_void_p(b['Handle']));time.sleep(.35)
    screenshot(name,(b['X'],b['Y'],b['X']+b['Width'],b['Y']+b['Height']))

background=subprocess.Popen([sys.executable,str(Path(__file__).resolve()),'--background'],creationflags=subprocess.CREATE_NO_WINDOW)
app=None
try:
    time.sleep(1.5)
    app=subprocess.Popen([str(exe)],creationflags=subprocess.CREATE_NO_WINDOW)
    time.sleep(2)
    assert len(list((session/'images').glob('*.png')))==10
    show();screenshot('01-welcome',(0,0,3072,480))
    for index,(a,b) in enumerate([((350,780),(1080,1390)),((1170,780),(1900,1390)),((1990,780),(2720,1390))]):
        u.SetCursorPos(*a);hotkey(65);drag(a,b)
        ui('confirm',window='随手挂 · 选择截图区域');time.sleep(.7)
    u.SetCursorPos(500,700);hotkey(65);drag((300,735),(1935,1435))
    ui('tool-rectangle',window='随手挂 · 选择截图区域');drag((380,825),(1040,1310))
    ui('tool-arrow',window='随手挂 · 选择截图区域');drag((1100,920),(1460,1025))
    # Capture actual editing UI, with the full toolbar and selection visible.
    screenshot('02-capture-editor',(180,650,2050,1700))
    ui('confirm',window='随手挂 · 选择截图区域');time.sleep(.7)
    show();screenshot('03-photo-rail',(0,0,3072,480))
    ui('open-history');time.sleep(.4);windowshot('04-history','随手挂 · 历史');ui(action='close',window='随手挂 · 历史')
    show();ui('open-settings');time.sleep(.4);windowshot('05-settings','随手挂 · 设置');ui(action='close',window='随手挂 · 设置')
    show()
    images=[row['Id'] for row in ui(action='list') if row['Id'].startswith('image-')]
    assert images
    b=ui(images[0],action='bounds');u.SetCursorPos(round(b['X']+b['Width']/2),round(b['Y']+b['Height']/2))
    for _ in range(2):u.mouse_event(2,0,0,0,0);u.mouse_event(4,0,0,0,0);time.sleep(.08)
    time.sleep(.5);windowshot('06-preview','随手挂 · 查看图片');press(27)
    (session.parent/'results.json').write_text(json.dumps({'welcome_seed_count':10,'new_captures':4,'screenshots':sorted(p.name for p in output.glob('*.png'))},indent=2))
    print('Documentation screenshots captured from actual Windows UI')
finally:
    if app:
        subprocess.run([str(exe),'--exit'],timeout=15,creationflags=subprocess.CREATE_NO_WINDOW)
        app.wait(timeout=15)
    background.terminate();background.wait(timeout=5)
