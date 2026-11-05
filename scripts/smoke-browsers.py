"""Real Edge/Chrome image paste and PNG file drop into a loopback-only page."""
from qa_paths import data_root, settings_root, launch_args

import ctypes,hashlib,http.server,json,os,subprocess,threading,time
from pathlib import Path
u=ctypes.windll.user32;u.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
project=Path(__file__).resolve().parent.parent;out=project/'artifacts'/'qa-v0.5';out.mkdir(parents=True,exist_ok=True)
ps=Path(os.environ['SystemRoot'])/'System32'/'WindowsPowerShell'/'v1.0'/'powershell.exe'
reports=[]
page=b'''<!doctype html><meta charset="utf-8"><title>SnapRail Local Browser QA</title>
<style>body{margin:0;background:#f1f7f4;font:26px Segoe UI}main{margin:40px;padding:70px;border:3px dashed #126b64;min-height:500px}textarea{font:24px Segoe UI;width:90%;height:100px}</style>
<main id="drop">SnapRail Local Browser QA<br>Drop PNG or paste image here<textarea placeholder="Click here, then Ctrl+V"></textarea><pre id="result"></pre></main>
<script>async function report(kind,files){for(const f of files){let bytes=await f.arrayBuffer();let digest=await crypto.subtle.digest('SHA-256',bytes);let hash=Array.from(new Uint8Array(digest)).map(x=>x.toString(16).padStart(2,'0')).join('');let info={kind,name:f.name,type:f.type,size:f.size,sha256:hash};result.textContent=JSON.stringify(info);await fetch('/report',{method:'POST',body:JSON.stringify(info)});}}
document.addEventListener('dragover',e=>e.preventDefault());document.addEventListener('drop',e=>{e.preventDefault();report('drop',e.dataTransfer.files)});document.addEventListener('paste',e=>{e.preventDefault();report('paste',e.clipboardData.files)});</script>'''
class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200);self.send_header('Content-Type','text/html; charset=utf-8');self.end_headers();self.wfile.write(page)
    def do_POST(self):
        reports.append(json.loads(self.rfile.read(int(self.headers['Content-Length']))));self.send_response(204);self.end_headers()
    def log_message(self,*args):pass
server=http.server.ThreadingHTTPServer(('127.0.0.1',0),Handler);threading.Thread(target=server.serve_forever,daemon=True).start()
def uia(id,action='invoke'):
    r=subprocess.run([str(ps),'-NoProfile','-ExecutionPolicy','Bypass','-File',str(project/'scripts'/'uia.ps1'),'-Window','SnapRail · 随手挂','-Action',action,'-Id',id],capture_output=True,timeout=15,creationflags=subprocess.CREATE_NO_WINDOW)
    assert r.returncode==0,r.stderr
    return json.loads(r.stdout.decode('utf-8-sig')) if r.stdout.strip() else None
def press(code):u.keybd_event(code,0,0,0);time.sleep(.04);u.keybd_event(code,0,2,0)
def click(x,y):u.SetCursorPos(x,y);time.sleep(.1);u.mouse_event(2,0,0,0,0);u.mouse_event(4,0,0,0,0);time.sleep(.15)
def drag(a,b):
    u.SetCursorPos(*a);time.sleep(.15);u.mouse_event(2,0,0,0,0)
    for i in range(1,45):u.SetCursorPos(round(a[0]+(b[0]-a[0])*i/44),round(a[1]+(b[1]-a[1])*i/44));time.sleep(.025)
    time.sleep(.3);u.mouse_event(4,0,0,0,0)
def show():u.keybd_event(17,0,0,0);u.keybd_event(18,0,0,0);press(84);u.keybd_event(18,0,2,0);u.keybd_event(17,0,2,0);time.sleep(.4)
def browser_window():
    matches=[];callback=ctypes.WINFUNCTYPE(ctypes.c_bool,ctypes.c_void_p,ctypes.c_void_p)
    @callback
    def visit(hwnd,_):
        title=ctypes.create_unicode_buffer(1024);u.GetWindowTextW(ctypes.c_void_p(hwnd),title,1024)
        if 'SnapRail Local Browser QA' in title.value and u.IsWindowVisible(ctypes.c_void_p(hwnd)):matches.append(hwnd)
        return True
    u.EnumWindows(visit,0);return matches[0] if matches else None
image=max((data_root()/'images').glob('*.png'),key=lambda p:p.stat().st_mtime)
id=image.stem.split('-')[-1];expected=hashlib.sha256(image.read_bytes()).hexdigest();results={}
try:
    for label,binary in [('edge',r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'),('chrome',r'C:\Program Files\Google\Chrome\Application\chrome.exe')]:
        if not Path(binary).exists():results[label]={'available':False};continue
        profile=out/(label+'-'+str(time.time_ns()));browser=subprocess.Popen([binary,'--user-data-dir='+str(profile),'--no-first-run','--no-default-browser-check','--disable-background-networking','--disable-sync','--app=http://127.0.0.1:'+str(server.server_port)],creationflags=subprocess.CREATE_NO_WINDOW)
        hwnd=None
        try:
            for _ in range(80):
                hwnd=browser_window()
                if hwnd:break
                time.sleep(.15)
            assert hwnd,'Browser QA window unavailable'
            u.SetWindowPos(ctypes.c_void_p(hwnd),0,350,500,1700,1100,0x40);u.SetForegroundWindow(ctypes.c_void_p(hwnd));time.sleep(.7)
            # Explicit copy via SnapRail, then paste into a real Chromium page.
            u.SetCursorPos(1500,0);time.sleep(.6);show();uia('image-'+id)
            u.SetForegroundWindow(ctypes.c_void_p(hwnd));time.sleep(.2)
            click(1000,850);before=len(reports);u.keybd_event(17,0,0,0);press(86);u.keybd_event(17,0,2,0)
            for _ in range(80):
                if len(reports)>before:break
                time.sleep(.1)
            assert len(reports)>before and reports[-1]['kind']=='paste' and reports[-1]['size']>0,(label,reports)
            pasted=reports[-1]
            u.SetCursorPos(1500,0);time.sleep(.7)
            # Hover opens the rail; query exact thumbnail coordinates.
            try:box=uia('image-'+id,'bounds')
            except AssertionError:show();box=uia('image-'+id,'bounds')
            before=len(reports);drag((round(box['X']+box['Width']/2),round(box['Y']+box['Height']/2)),(1100,1100))
            for _ in range(80):
                if len(reports)>before:break
                time.sleep(.1)
            assert len(reports)>before and reports[-1]['kind']=='drop' and reports[-1]['sha256']==expected,(label,reports)
            results[label]={'image_paste':True,'png_drop_byte_exact':True,'paste_type':pasted['type']}
        finally:
            if hwnd:u.PostMessageW(ctypes.c_void_p(hwnd),0x10,0,0)
            try:browser.wait(timeout=8)
            except subprocess.TimeoutExpired:browser.terminate();browser.wait(timeout=5)
finally:
    server.shutdown();(out/'browser-results.json').write_text(json.dumps(results,indent=2),encoding='utf-8');print(json.dumps(results,indent=2))
