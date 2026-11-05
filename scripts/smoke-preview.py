"""Real single/double click, preview scale and capture exclusion checks."""
from qa_paths import data_root, settings_root, launch_args

import ctypes,json,os,subprocess,time
from pathlib import Path
from PIL import Image,ImageGrab,ImageChops
u=ctypes.windll.user32;u.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
project=Path(__file__).resolve().parent.parent;out=project/'artifacts'/'qa-v0.5.2';out.mkdir(parents=True,exist_ok=True)
ps=Path(os.environ['SystemRoot'])/'System32/WindowsPowerShell/v1.0/powershell.exe'
root=settings_root();settings=root/'settings.json'
data=data_root()
path=max((data/'images').glob('*.png'),key=lambda p:p.stat().st_mtime_ns);id=path.stem.split('-')[-1]
def uia(id=None,action='invoke',window='随手挂 · 查看图片'):
    args=[str(ps),'-NoProfile','-ExecutionPolicy','Bypass','-File',str(project/'scripts'/'uia.ps1'),'-Window',window,'-Action',action]
    if id:args+=['-Id',id]
    r=subprocess.run(args,capture_output=True,timeout=15,creationflags=subprocess.CREATE_NO_WINDOW)
    assert r.returncode==0,r.stderr.decode('utf-8',errors='replace')
    return json.loads(r.stdout.decode('utf-8-sig')) if r.stdout.strip() else None
def key(code,up=False):u.keybd_event(code,0,2 if up else 0,0)
def press(code):key(code);time.sleep(.035);key(code,True)
def click():u.mouse_event(2,0,0,0,0);time.sleep(.025);u.mouse_event(4,0,0,0,0)
def reveal():u.SetCursorPos(1500,0);time.sleep(.7)
results={}
reveal();box=uia('image-'+id,'bounds','SnapRail · 随手挂')
u.SetCursorPos(round(box['X']+box['Width']/2),round(box['Y']+box['Height']/2));click();time.sleep(.7)
pasted=ImageGrab.grabclipboard();assert isinstance(pasted,Image.Image)
assert ImageChops.difference(pasted.convert('RGB'),Image.open(path).convert('RGB')).getbbox() is None
results['single_click_copies_full_image']=True
try:uia(action='window');raise RuntimeError('Single click opened preview')
except AssertionError:pass
click();time.sleep(.08);click();time.sleep(.6);uia(action='window')
results['real_double_click_opens_preview']=True
uia('preview-actual');info=uia('preview-info','read')['Name'];assert '100%' in info,info
uia('preview-zoom-in');info=uia('preview-info','read')['Name'];assert '125%' in info,info
uia('preview-fit');results['actual_size_zoom_and_fit']=True
key(17);key(18);press(65);key(18,True);key(17,True);time.sleep(.7)
uia(action='window',window='随手挂 · 选择截图区域')
try:uia(action='window');raise RuntimeError('Preview remained visible in capture')
except AssertionError:pass
press(27);time.sleep(.4);uia(action='window');results['capture_hides_and_restores_preview']=True
ImageGrab.grab().save(out/'preview-desktop.png');press(27);time.sleep(.4)
try:uia(action='window');raise RuntimeError('Esc did not close preview')
except AssertionError:pass
results['escape_closes_preview']=True
(out/'preview-results.json').write_text(json.dumps(results,indent=2),encoding='utf-8');print(json.dumps(results,indent=2))
