"""Responsive known pixels on every monitor, used by multi-monitor QA."""
import ctypes
import json
from pathlib import Path
import sys
import tkinter as tk
from monitors import monitors

u = ctypes.windll.user32
u.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
root = tk.Tk(); root.withdraw()
for index, monitor in enumerate(monitors()):
    left, top, right, bottom = monitor['bounds']
    window = tk.Toplevel(root); window.overrideredirect(True)
    canvas = tk.Canvas(window, width=right-left, height=bottom-top, background='#e6f2ee' if index == 0 else '#dbe9f3', highlightthickness=0)
    canvas.pack(fill='both', expand=True)
    for x in range(0, right-left, 40):
        for y in range(0, bottom-top, 40):
            canvas.create_rectangle(x,y,x+20,y+20,fill='#126b64' if index == 0 else '#2675d8',outline='')
    canvas.create_text(400,180,text=f'SnapRail / monitor {index+1}',font=('Segoe UI',26,'bold'),fill='#253a40')
    window.update()
    hwnd = u.GetAncestor(ctypes.c_void_p(window.winfo_id()), 2)
    u.SetWindowPos(ctypes.c_void_p(hwnd), ctypes.c_void_p(-1), left, top, right-left, bottom-top, 0x40)
Path(sys.argv[1]).write_text(json.dumps(monitors()), encoding='utf-8')
root.mainloop()
