"""Release regression for the newest interactions; requires an isolated test profile."""
import ctypes
import json
import os
import subprocess
import time
from pathlib import Path
from qa_paths import data_root, launch_args

assert os.environ.get('SNAPRAIL_TEST_PROFILE'),'Run only against an explicit isolated test profile'
project=Path(__file__).resolve().parent.parent
output=project/'artifacts'/'qa-final';output.mkdir(parents=True,exist_ok=True)
exe=Path(os.environ['SNAPRAIL_TEST_EXE']);root=data_root()
ps=Path(os.environ['SystemRoot'])/'System32/WindowsPowerShell/v1.0/powershell.exe'
u=ctypes.windll.user32;u.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
results={}

def ui(action='invoke',id=None,name=None,window='SnapRail · 随手挂',text=None):
    args=[str(ps),'-NoProfile','-ExecutionPolicy','Bypass','-File',str(project/'scripts/uia.ps1'),'-Window',window,'-Action',action]
    if id:args+=['-Id',id]
    if name:args+=['-Name',name]
    if text is not None:args+=['-Text',str(text)]
    r=subprocess.run(args,capture_output=True,timeout=15,creationflags=subprocess.CREATE_NO_WINDOW)
    assert r.returncode==0,r.stderr.decode('utf-8',errors='replace')
    return json.loads(r.stdout.decode('utf-8-sig')) if r.stdout.strip() else None

def reveal():
    u.SetCursorPos(1536,0)
    subprocess.run(launch_args(exe),creationflags=subprocess.CREATE_NO_WINDOW,timeout=12)
    time.sleep(.3)

def state():return json.loads((root/'library.json').read_text(encoding='utf-8-sig'))['Items']
def context(id):
    reveal();box=ui('bounds','image-'+id)
    u.SetCursorPos(round(box['X']+box['Width']/2),round(box['Y']+box['Height']/2))
    u.mouse_event(8,0,0,0,0);u.mouse_event(16,0,0,0,0);time.sleep(.1)

def drag(a,b,cancel=False):
    x=round(a['X']+a['Width']/2);y=round(a['Y']+a['Height']/2)
    tx=round(b['X']+b['Width']/2+12);ty=round(b['Y']+b['Height']/2)
    u.SetCursorPos(x,y);u.mouse_event(2,0,0,0,0)
    for i in range(1,26):u.SetCursorPos(round(x+(tx-x)*i/25),round(y+(ty-y)*i/25));time.sleep(.02)
    if cancel:u.keybd_event(27,0,0,0);u.keybd_event(27,0,2,0)
    u.mouse_event(4,0,0,0,0);time.sleep(.8)

originals={}
try:
    reveal();rows=ui('list');ids=[row['Id'][6:] for row in rows if row['Id'].startswith('image-')][:4]
    assert len(ids)==4
    originals={id:state()[id]['Favorite'] for id in ids}
    assert not any(item['Favorite'] for item in state().values()),'Profile should start with no favorites'
    for index,id in enumerate(ids):
        context(id);ui(name='固定收藏');time.sleep(.65)
        assert sum(item['Favorite'] and not item.get('Deleted',False) for item in state().values())==min(index+1,3)
    context(ids[3]);assert ui('read',name='固定收藏')['ToggleState']=='Off'
    u.keybd_event(27,0,0,0);u.keybd_event(27,0,2,0)
    reveal();boxes=[ui('bounds','image-'+id) for id in ids]
    assert all(boxes[i]['X']<boxes[3]['X'] for i in range(3))
    results['favorite_limit_left_group_and_rejected_checkbox']=True
finally:
    for id,old in originals.items():
        if state().get(id,{}).get('Favorite')!=old:
            context(id);ui(name='取消固定收藏' if not old else '固定收藏');time.sleep(.6)

reveal();time.sleep(1.8)
rows=ui('list');ids=[row['Id'][6:] for row in rows if row['Id'].startswith('image-')][:2]
a,b=[ui('bounds','image-'+id) for id in ids]
u.SetCursorPos(round(a['X']+a['Width']/2),round(a['Y']+a['Height']/2));u.mouse_event(2,0,0,0,0);time.sleep(.25)
try:
    raised=ui('bounds','image-'+ids[0]);lowered=ui('bounds','image-'+ids[1])
    assert raised['Width']>a['Width']*1.04 and lowered['Width']<b['Width']*.97
finally:u.mouse_event(4,0,0,0,0)
time.sleep(.4)
a,b=[ui('bounds','image-'+id) for id in ids];drag(a,b)
reveal();a,b=[ui('bounds','image-'+id) for id in ids];assert a['X']>b['X']
ranking={id:state()[id].get('RailOrderUtcTicks',0) for id in ids};drag(a,b,True)
assert ranking=={id:state()[id].get('RailOrderUtcTicks',0) for id in ids}
results['lift_background_shrink_reorder_settle_and_escape']=True

reveal();ui(id='open-settings');time.sleep(.4)
window='随手挂 · 设置';bounds=ui('window',window=window)
for id in ['settings-capture','settings-directory','settings-screen','settings-photo-width','settings-top','settings-hover-delay','settings-hide-delay','settings-save']:
    box=ui('bounds',id,window=window)
    assert box['Width']>0 and bounds['Y']<=box['Y'] and box['Y']+box['Height']<bounds['Y']+bounds['Height']-10
ui('range','settings-photo-width',window=window,text=195)
slider=ui('bounds','settings-photo-width',window=window)
u.SetCursorPos(round(slider['X']+8),round(slider['Y']+slider['Height']/2));u.mouse_event(2,0,0,0,0);u.mouse_event(4,0,0,0,0);time.sleep(.2)
labels=ui('list',window=window);assert any('挂图尺寸 · 185 px'==row['Name'] for row in labels)
ui('range','settings-photo-width',window=window,text=150);ui('close',window=window)
results['all_settings_visible_and_slider_track_click_changes_value']=True

reveal();ui(id='open-history');time.sleep(.3);window='随手挂 · 历史'
ui('select','history-filter-收藏',window=window);assert '共 0 张' in ui('read','history-count',window=window)['Name']
ui('select','history-filter-全部',window=window);ui('close',window=window)
results['segmented_history_filter']=True
(output/'final-interactions.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
print(json.dumps(results,indent=2))
