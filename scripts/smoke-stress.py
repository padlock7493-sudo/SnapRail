"""100 real captures in an isolated profile; user library/configuration are untouched."""
from qa_paths import data_root, settings_root, launch_args

import ctypes
from ctypes import wintypes as w
import json
import hashlib
import os
from pathlib import Path
import subprocess
import sys
import time
from monitors import monitors

u=ctypes.windll.user32;k=ctypes.windll.kernel32
u.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
k.OpenProcess.restype=ctypes.c_void_p
project=Path(__file__).resolve().parent.parent;output=project/'artifacts'/os.environ.get('SNAPRAIL_QA_FOLDER','qa-v0.4');output.mkdir(parents=True,exist_ok=True)
profile=output/('stress-'+str(time.time_ns()));profile.mkdir(parents=True)
capture_count=int(os.environ.get('SNAPRAIL_STRESS_COUNT','100'));assert 60<=capture_count<=1000
idle_seconds=float(os.environ.get('SNAPRAIL_IDLE_SECONDS','2.5'));assert 2.5<=idle_seconds<=3600
exe=Path(os.environ.get('SNAPRAIL_TEST_EXE',str(project/'src'/'SnapRail.App'/'bin'/'Release'/'net10.0-windows'/'SnapRail.exe')))
env=os.environ.copy();env['DOTNET_ROOT']=str(Path.home()/'.dotnet')
def run(args):return subprocess.run(args,env=env,capture_output=True,timeout=12,creationflags=subprocess.CREATE_NO_WINDOW)
def key(code,up=False):u.keybd_event(code,0,2 if up else 0,0)
def press(code):key(code);time.sleep(.025);key(code,True)
def capture_hotkey():key(17);key(18);time.sleep(.035);press(65);key(18,True);key(17,True)

class Memory(ctypes.Structure):
    _fields_=[('cb',w.DWORD),('faults',w.DWORD),('peak_rss',ctypes.c_size_t),('rss',ctypes.c_size_t),('peak_paged',ctypes.c_size_t),('paged',ctypes.c_size_t),('peak_nonpaged',ctypes.c_size_t),('nonpaged',ctypes.c_size_t),('pagefile',ctypes.c_size_t),('peak_pagefile',ctypes.c_size_t),('private',ctypes.c_size_t)]
def snapshot(handle):
    memory=Memory();memory.cb=ctypes.sizeof(memory)
    assert ctypes.windll.psapi.GetProcessMemoryInfo(ctypes.c_void_p(handle),ctypes.byref(memory),memory.cb)
    return dict(private_mib=round(memory.private/1024**2,2),working_set_mib=round(memory.rss/1024**2,2),gdi=u.GetGuiResources(ctypes.c_void_p(handle),0),user=u.GetGuiResources(ctypes.c_void_p(handle),1))
def cpu(handle):
    created,exited,kernel,user=w.FILETIME(),w.FILETIME(),w.FILETIME(),w.FILETIME()
    assert k.GetProcessTimes(ctypes.c_void_p(handle),ctypes.byref(created),ctypes.byref(exited),ctypes.byref(kernel),ctypes.byref(user))
    return (((kernel.dwHighDateTime<<32)|kernel.dwLowDateTime)+((user.dwHighDateTime<<32)|user.dwLowDateTime))/10**7

run([str(exe),'--exit']);time.sleep(.9)
target=subprocess.Popen([sys.executable,str(project/'scripts'/'qa-target.py')],creationflags=subprocess.CREATE_NO_WINDOW)
source=subprocess.Popen([str(exe),'--profile',str(profile)],env=env,creationflags=subprocess.CREATE_NO_WINDOW)
handle=None;results={'profile':str(profile),'samples':[]};images=profile/'images'
layout=monitors();virtual_width=max(m['bounds'][2] for m in layout)-min(m['bounds'][0] for m in layout)
callback_type=ctypes.WINFUNCTYPE(w.BOOL,ctypes.c_void_p,ctypes.c_void_p)
def overlay():
    found=[]
    @callback_type
    def visit(hwnd,_):
        pid=w.DWORD();u.GetWindowThreadProcessId(ctypes.c_void_p(hwnd),ctypes.byref(pid))
        if pid.value==source.pid and u.IsWindowVisible(ctypes.c_void_p(hwnd)):
            rect=w.RECT();u.GetWindowRect(ctypes.c_void_p(hwnd),ctypes.byref(rect))
            if rect.right-rect.left>=virtual_width-2:found.append(hwnd)
        return True
    u.EnumWindows(visit,0)
    return found[0] if found else None
try:
    time.sleep(1);assert source.poll() is None,'Profile failed to start'
    handle=k.OpenProcess(0x410,False,source.pid);assert handle
    elapsed=[];test_started=time.monotonic()
    for count in range(1,capture_count+1):
        u.SetCursorPos(500,750);capture_hotkey()
        deadline=time.monotonic()+4
        while not overlay() and time.monotonic()<deadline:time.sleep(.015)
        assert overlay(),'Capture overlay missing'
        time.sleep(.09);u.SetCursorPos(450,650);u.mouse_event(2,0,0,0,0)
        for step in range(1,12):u.SetCursorPos(round(450+300*step/11),round(650+200*step/11));time.sleep(.007)
        u.mouse_event(4,0,0,0,0);time.sleep(.03)
        start=time.monotonic();press(13)
        deadline=start+5
        while len(list(images.glob('*.png')))<count and time.monotonic()<deadline:time.sleep(.015)
        assert len(list(images.glob('*.png')))==count,(count,'capture count mismatch')
        elapsed.append((time.monotonic()-start)*1000)
        if count%10==0:
            sample=snapshot(handle);sample['capture']=count;results['samples'].append(sample)
    time.sleep(1.5)
    before=cpu(handle);start=time.monotonic();time.sleep(idle_seconds);single_cpu=100*(cpu(handle)-before)/(time.monotonic()-start)
    results.update(captures=capture_count,duration_seconds=round(time.monotonic()-test_started,1),idle_sample_seconds=idle_seconds,confirmation_to_file_median_ms=round(sorted(elapsed)[capture_count//2],1),confirmation_to_file_p95_ms=round(sorted(elapsed)[int(capture_count*.95)-1],1),idle_cpu_single_core_percent=round(single_cpu,3),idle_cpu_machine_percent=round(single_cpu/(os.cpu_count() or 1),3))
    # The first 30 captures populate the bounded rail thumbnail cache. Compare
    # the whole post-warm-up interval (40..100), rather than an empty cache.
    warmed=results['samples'][3:]
    assert max(s['gdi'] for s in warmed)-min(s['gdi'] for s in warmed)<=8,'GDI handles continued growing after cache warm-up'
    assert results['samples'][-1]['private_mib']-results['samples'][4]['private_mib']<192,'Private memory continued growing excessively after warm-up'
    results['capture_resource_checks']=True
    if os.environ.get('SNAPRAIL_CRASH_RECOVERY')=='1':
        # Only terminate this script's newly created isolated profile process.
        # All captures have completed and the user profile is not in use here.
        signatures={str(p.relative_to(profile)):hashlib.sha256(p.read_bytes()).hexdigest() for folder in [images,profile/'documents'] for p in folder.rglob('*') if p.is_file()}
        source.terminate();source.wait(timeout=8)
        source=subprocess.Popen([str(exe),'--profile',str(profile)],env=env,creationflags=subprocess.CREATE_NO_WINDOW)
        time.sleep(1.5);assert source.poll() is None,'Recovery instance did not start'
        assert len(list(images.glob('*.png')))==capture_count
        for relative,digest in signatures.items():assert hashlib.sha256((profile/relative).read_bytes()).hexdigest()==digest,relative
        json.loads((profile/'library.json').read_text(encoding='utf-8-sig'))
        results['isolated_abrupt_exit_reopens_saved_png_and_annotation_bytes_unchanged']=True
finally:
    if source.poll() is None:
        active=overlay()
        if active:u.SetForegroundWindow(ctypes.c_void_p(active));press(27)
        run([str(exe),'--exit'])
        try:source.wait(timeout=8)
        except subprocess.TimeoutExpired:source.terminate();source.wait(timeout=5)
    if handle:k.CloseHandle(ctypes.c_void_p(handle))
    target.terminate();target.wait(timeout=5)
    subprocess.Popen(launch_args(exe),env=env,creationflags=subprocess.CREATE_NO_WINDOW)
    (output/'stress-results.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(results,ensure_ascii=False,indent=2))
