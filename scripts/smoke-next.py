"""v0.5 real desktop checks: window recognition, object edits, OCR and scrolling."""
from qa_paths import data_root, settings_root, launch_args

import ctypes,json,os,subprocess,sys,time
from pathlib import Path
from PIL import Image,ImageChops
u=ctypes.windll.user32;u.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
project=Path(__file__).resolve().parent.parent;output=project/'artifacts'/'qa-v0.5';output.mkdir(exist_ok=True,parents=True)
ready=output/('target-'+str(time.time_ns())+'.json')
target=subprocess.Popen([sys.executable,str(project/'scripts'/'qa-next-target.py'),str(ready)],creationflags=subprocess.CREATE_NO_WINDOW)
images=data_root()/'images';ps=Path(os.environ['SystemRoot'])/'System32'/'WindowsPowerShell'/'v1.0'/'powershell.exe'
results={}
def uia(id=None,action='invoke',window='随手挂 · 选择截图区域',text=None,name=None):
    args=[str(ps),'-NoProfile','-ExecutionPolicy','Bypass','-File',str(project/'scripts'/'uia.ps1'),'-Window',window,'-Action',action]
    if id:args+=['-Id',id]
    if text is not None:args+=['-Text',text]
    if name:args+=['-Name',name]
    r=subprocess.run(args,capture_output=True,timeout=15,creationflags=subprocess.CREATE_NO_WINDOW)
    assert r.returncode==0,r.stderr.decode('utf-8',errors='replace')
    return json.loads(r.stdout.decode('utf-8-sig')) if r.stdout.strip() else None
def key(code,up=False):u.keybd_event(code,0,2 if up else 0,0)
def press(code):key(code);time.sleep(.04);key(code,True)
def shortcut(code,alt=False):
    key(17)
    if alt:key(18)
    press(code)
    if alt:key(18,True)
    key(17,True)
def click(x,y):
    u.SetCursorPos(round(x),round(y));time.sleep(.08);u.mouse_event(2,0,0,0,0);u.mouse_event(4,0,0,0,0);time.sleep(.12)
def drag(a,b):
    u.SetCursorPos(*a);time.sleep(.08);u.mouse_event(2,0,0,0,0)
    for step in range(1,20):u.SetCursorPos(round(a[0]+(b[0]-a[0])*step/19),round(a[1]+(b[1]-a[1])*step/19));time.sleep(.015)
    u.mouse_event(4,0,0,0,0);time.sleep(.15)
def capture():shortcut(65,True);time.sleep(.7)
def wait_file(before):
    for _ in range(90):
        new=set(images.glob('*.png'))-before
        if new:
            path=next(iter(new));time.sleep(.4);return path
        time.sleep(.1)
    raise AssertionError('No saved image')
try:
    for _ in range(100):
        if ready.exists():break
        time.sleep(.1)
    fixture=json.loads(ready.read_text());time.sleep(.5)
    before=set(images.glob('*.png'));capture();u.SetCursorPos(650,800);time.sleep(.3);click(650,800);press(13)
    window=wait_file(before);assert Image.open(window).width>=1500 and Image.open(window).height>=1000
    results['single_click_window_recognition']=True
    before=set(images.glob('*.png'));capture();drag((450,650),(1250,1150))
    uia('tool-rectangle');drag((500,900),(700,1050))
    uia('tool-object');drag((600,950),(650,980));uia('color-2675D8')
    # Refocus the object with a single click before nudging/deleting it.
    click(650,980);press(39);press(46);shortcut(90);shortcut(89);shortcut(90)
    uia('tool-text');click(850,750);uia('annotation-text','value',text='ORIGINAL');shortcut(13)
    uia('tool-object');click(870,765);click(870,765);time.sleep(.2)
    uia('annotation-text','value',text='UPDATED');shortcut(13)
    uia('ocr');time.sleep(.8)
    deadline=time.monotonic()+30;recognized=''
    while time.monotonic()<deadline:
        recognized=uia('ocr-text','read','随手挂 · 文字识别').get('Value','')
        if recognized:break
        time.sleep(.4)
    assert 'OCR' in str(recognized) and '2026' in str(recognized),recognized
    uia(action='close',window='随手挂 · 文字识别');results['local_ocr_from_selection']=True
    uia('confirm');edited=wait_file(before)
    document=json.loads((images.parent/'documents'/edited.stem.split('-')[-1]/'annotations.json').read_text(encoding='utf-8-sig'))
    assert len(document['Marks'])==2,document
    rect=document['Marks'][0];assert rect['Color']=='#2675D8' and abs(rect['Points'][0]['X']-101)<1.1 and abs(rect['Points'][0]['Y']-280)<1.1,rect
    assert document['Marks'][1]['Text']=='UPDATED',document
    results['object_move_recolor_nudge_delete_undo_redo_and_text_edit']=True
    capture();drag((450,650),(1250,1150));uia('scroll-capture');time.sleep(.8)
    uia('scroll-start',window='随手挂 · 滚动长截图');time.sleep(4)
    if uia('scroll-stop','bounds','随手挂 · 滚动长截图')['Enabled']:uia('scroll-stop',window='随手挂 · 滚动长截图')
    time.sleep(1)
    status=uia('scroll-status','read','随手挂 · 滚动长截图');print('scroll status:',ascii(str(status)))
    before=set(images.glob('*.png'));uia('scroll-finish',window='随手挂 · 滚动长截图');time.sleep(.6)
    if os.environ.get('SNAPRAIL_TEST_UNSAVED')=='1':
        editor=uia(action='window',window='随手挂 · 编辑截图')
        u.PostMessageW(ctypes.c_void_p(editor['Handle']),0x10,0,0);time.sleep(.3)
        dialog=uia(action='window',window='尚未保存的标注')
        u.GetDlgItem.restype=ctypes.c_void_p;no=u.GetDlgItem(ctypes.c_void_p(dialog['Handle']),7);assert no
        u.SendMessageW(ctypes.c_void_p(no),0xF5,0,0);time.sleep(.3)
        assert uia(action='window',window='随手挂 · 编辑截图')
        assert set(images.glob('*.png'))==before
        results['unsaved_long_preview_close_requires_confirmation_and_no_keeps_image']=True
    uia('confirm',window='随手挂 · 编辑截图')
    long=wait_file(before)
    with Image.open(long) as im:actual=im.convert('RGB')
    assert actual.height>500,actual.size
    expected=Image.open(ready.with_suffix('.png')).crop((450-fixture['x'],650-fixture['y'],1250-fixture['x'],650-fixture['y']+actual.height)).convert('RGB')
    assert ImageChops.difference(actual,expected).getbbox() is None,'Long screenshot seams differ from source page'
    results['real_auto_scroll_pixel_exact_stitch_preview_and_save']=True
    results['long_image_size']=actual.size
finally:
    target.terminate();target.wait(timeout=5)
    (output/'next-results.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(results,ensure_ascii=False,indent=2))
