"""History, real Recycle Bin, hotkey conflict, migration, startup and settings desktop QA.
Only deletes the image created by this test, and restores it. Restores initial settings/Run value.
"""
from qa_paths import data_root, settings_root, launch_args
from monitors import monitors

import ctypes
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import winreg
from PIL import Image, ImageGrab

u=ctypes.windll.user32;u.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
project=Path(__file__).resolve().parent.parent
output=project/'artifacts'/'qa-v0.4';output.mkdir(parents=True,exist_ok=True)
root=settings_root();config_path=root/'settings.json'
original_configuration=config_path.read_bytes() if config_path.exists() else None
initial=json.loads(original_configuration.decode('utf-8-sig')) if original_configuration else {}
data=Path(initial.get('DataRoot',str(root)));images=data/'images'
exe=Path(os.environ.get('SNAPRAIL_TEST_EXE',str(project/'src'/'SnapRail.App'/'bin'/'Release'/'net10.0-windows'/'SnapRail.exe')))
env=os.environ.copy();env['DOTNET_ROOT']=str(Path.home()/'.dotnet')
ps=Path(os.environ['SystemRoot'])/'System32'/'WindowsPowerShell'/'v1.0'/'powershell.exe'
run_key=r'Software\Microsoft\Windows\CurrentVersion\Run'
try:
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,run_key) as registry: old_run=winreg.QueryValueEx(registry,'SnapRail')
except FileNotFoundError: old_run=None

def call(args,**kwargs):return subprocess.run(args,capture_output=True,timeout=15,creationflags=subprocess.CREATE_NO_WINDOW,**kwargs)
def uia(id=None,action='invoke',window='SnapRail · 随手挂',text=None):
    args=[str(ps),'-NoProfile','-ExecutionPolicy','Bypass','-File',str(project/'scripts'/'uia.ps1'),'-Window',window,'-Action',action]
    if id:args+=['-Id',id]
    if text is not None:args+=['-Text',text]
    result=call(args)
    assert result.returncode==0,result.stderr.decode('utf-8',errors='replace')
    return json.loads(result.stdout.decode('utf-8-sig')) if result.stdout.strip() else None
def wait(predicate,timeout=12):
    end=time.monotonic()+timeout
    while time.monotonic()<end:
        if predicate():return
        time.sleep(.12)
    raise AssertionError('Management state not ready')
def key(k,up=False):u.keybd_event(k,0,2 if up else 0,0)
def press(k):key(k);time.sleep(.05);key(k,True)
def hotkey(letter):key(17);key(18);time.sleep(.05);press(ord(letter));key(18,True);key(17,True);time.sleep(.15)
def drag(a,b):
    u.SetCursorPos(*a);u.mouse_event(2,0,0,0,0)
    for i in range(1,20):u.SetCursorPos(round(a[0]+(b[0]-a[0])*i/19),round(a[1]+(b[1]-a[1])*i/19));time.sleep(.02)
    u.mouse_event(4,0,0,0,0);time.sleep(.15)
def show():call([str(exe)],env=env);time.sleep(.5)
def select_history(id):
    uia('history-search','value','随手挂 · 历史',id);time.sleep(.55)
    uia('history-'+id,'select','随手挂 · 历史')
def state(id):
    try:return json.loads((data/'library.json').read_text())['Items'].get(id,{})
    except (OSError,json.JSONDecodeError):return {}
def new_file(before,directory):
    wait(lambda:bool(set(directory.glob('*.png'))-before))
    path=next(iter(set(directory.glob('*.png'))-before));time.sleep(.2);return path
def settings_status():return uia('settings-status','read','随手挂 · 设置')['Name']

results={};target=subprocess.Popen([sys.executable,str(project/'scripts'/'qa-target.py')],creationflags=subprocess.CREATE_NO_WINDOW);reservation=None
try:
    time.sleep(.7)
    before=set(images.glob('*.png'));hotkey('A');time.sleep(.8);drag((450,650),(950,950))
    uia('tool-rectangle',window='随手挂 · 选择截图区域');drag((500,700),(750,850));uia('confirm',window='随手挂 · 选择截图区域')
    image=new_file(before,images);id=image.stem.split('-')[-1]
    image_hash=hashlib.sha256(image.read_bytes()).hexdigest()
    annotation=data/'documents'/id/'annotations.json';annotation_hash=hashlib.sha256(annotation.read_bytes()).hexdigest()
    show();uia('open-history');time.sleep(.5);select_history(id)
    uia('history-hide',window='随手挂 · 历史');wait(lambda:state(id).get('Hidden') is True)
    select_history(id);uia('history-hang',window='随手挂 · 历史');wait(lambda:state(id).get('Hidden') is False)
    results['history_search_take_down_and_rehang']=True
    select_history(id);uia('history-delete',window='随手挂 · 历史');time.sleep(.25)
    dialog=uia(action='window',window='删除截图')
    u.GetDlgItem.restype=ctypes.c_void_p
    yes=u.GetDlgItem(ctypes.c_void_p(dialog['Handle']),6)
    assert yes,'Native Yes button missing'
    button_class=ctypes.create_unicode_buffer(64);u.GetClassNameW(ctypes.c_void_p(yes),button_class,64)
    assert button_class.value.lower()=='button',button_class.value
    u.SendMessageW(ctypes.c_void_p(yes),0xF5,0,0)
    wait(lambda:not image.exists() and state(id).get('Deleted') is True)
    assert not annotation.exists()
    results['real_recycle_bin_bundles_image_and_annotations']=True
    uia('history-undo-delete',window='随手挂 · 历史')
    wait(lambda:image.exists() and annotation.exists() and state(id).get('Deleted') is False)
    assert hashlib.sha256(image.read_bytes()).hexdigest()==image_hash
    assert hashlib.sha256(annotation.read_bytes()).hexdigest()==annotation_hash
    results['real_recycle_bin_restore_is_byte_exact']=True
    ImageGrab.grab().resize((1536,960)).save(output/'history.png')
    uia(action='close',window='随手挂 · 历史')

    # Reserve Q in a separate process to exercise OS registration failure and rollback.
    ready=output/('hotkey-'+str(time.time_ns())+'.ready')
    dll=project/'tests'/'SnapRail.Tests'/'bin'/'Release'/'net10.0-windows'/'SnapRail.Tests.dll'
    reservation=subprocess.Popen([str(Path.home()/'.dotnet'/'dotnet.exe'),str(dll),'--reserve-hotkey',str(ready)],env=env,creationflags=subprocess.CREATE_NO_WINDOW)
    wait(ready.exists)
    assert reservation.poll() is None,'Hotkey holder exited'
    show();uia('open-settings');time.sleep(.3)
    uia('settings-capture','focus','随手挂 · 设置');hotkey('Q')
    assert uia('settings-capture','read','随手挂 · 设置')['Value'].endswith('Q'),'Shortcut recorder did not record Q'
    uia('settings-save',window='随手挂 · 设置');time.sleep(.5)
    assert '占用' in settings_status(),settings_status()
    assert (config_path.read_bytes() if config_path.exists() else None)==original_configuration
    uia('settings-save','focus','随手挂 · 设置')
    hotkey('A');time.sleep(.6);uia(action='window',window='随手挂 · 选择截图区域');press(27);time.sleep(.3)
    results['occupied_hotkey_rejected_and_old_binding_works']=True
    reservation.terminate();reservation.wait(timeout=5);reservation=None

    # Save actual UI settings, copy the library to an empty E-drive directory, and restart.
    destination=output/('data-'+str(time.time_ns()))
    source_files={file.name:hashlib.sha256(file.read_bytes()).hexdigest() for file in images.glob('*.png')}
    uia('settings-directory','value','随手挂 · 设置',str(destination))
    uia('settings-hover','toggle','随手挂 · 设置')
    uia('settings-startup','toggle','随手挂 · 设置')
    uia('settings-screen','focus','随手挂 · 设置');press(0x23);press(9)
    if os.environ.get('SNAPRAIL_TEST_TUNING')=='1':
        for control,value in [('settings-photo-width',220),('settings-top',40),('settings-hover-delay',500),('settings-hide-delay',1200)]:uia(control,'range','随手挂 · 设置',str(value))
    ImageGrab.grab().resize((1536,960)).save(output/'settings.png')
    uia('settings-save',window='随手挂 · 设置')
    wait(lambda:config_path.exists() and json.loads(config_path.read_text()).get('DataRoot')==str(destination),30)
    time.sleep(1.6)
    for filename,digest in source_files.items():
        assert hashlib.sha256((destination/'images'/filename).read_bytes()).hexdigest()==digest
        assert hashlib.sha256((images/filename).read_bytes()).hexdigest()==digest
    assert hashlib.sha256((destination/'documents'/id/'annotations.json').read_bytes()).hexdigest()==annotation_hash
    results['cross_drive_library_migration_verified_and_source_preserved']=True
    cfg=json.loads(config_path.read_text());assert cfg['Capture']['Key']==0x51 and not cfg['HoverEnabled']
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,run_key) as registry: assert 'SnapRail.exe' in winreg.QueryValueEx(registry,'SnapRail')[0]
    results['startup_option_writes_user_run_entry']=True
    before=set((destination/'images').glob('*.png'));hotkey('Q');time.sleep(.7);drag((450,650),(950,950));press(13)
    new_file(before,destination/'images')
    results['custom_hotkey_active_after_restart']=True
    show();rail=uia(action='window')
    monitor=next(item for item in monitors() if item['device']==cfg['RailScreen'])
    wx,wy,wr,wb=monitor['work'];assert wx<=rail['X'] and rail['X']+rail['Width']<=wr+2,rail
    results['fixed_display_setting']=True
    if os.environ.get('SNAPRAIL_TEST_TUNING')=='1':
        assert cfg['PhotoWidth']==220 and cfg['RailTopMargin']==40 and cfg['HoverDelayMs']==500 and cfg['HideDelayMs']==1200,cfg
        assert rail['Height']>400 and abs(rail['Y']-(wy+int(40*monitor['dpi']/96)))<=2,rail
        results['photo_size_top_margin_and_delays_persist_and_render_after_restart']=True
finally:
    if reservation:reservation.terminate();reservation.wait(timeout=5)
    try:uia('cancel',window='随手挂 · 选择截图区域')
    except AssertionError:pass
    # Restore only the exact configuration and Run value snapshotted before this test.
    call([str(exe),'--exit'],env=env);time.sleep(.8)
    if original_configuration is None:
        if config_path.exists():config_path.unlink()
    else:config_path.write_bytes(original_configuration)
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER,run_key) as registry:
        if old_run is None:
            try:winreg.DeleteValue(registry,'SnapRail')
            except FileNotFoundError:pass
        else:winreg.SetValueEx(registry,'SnapRail',0,old_run[1],old_run[0])
    subprocess.Popen(launch_args(exe),env=env,creationflags=subprocess.CREATE_NO_WINDOW)
    target.terminate();target.wait(timeout=5)
    (output/'management-results.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(results,ensure_ascii=False,indent=2))
