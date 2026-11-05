"""Physical monitor inventory for desktop QA (does not change display settings)."""
import ctypes
from ctypes import wintypes as w
import json

u = ctypes.windll.user32
u.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))

class MonitorInfo(ctypes.Structure):
    _fields_ = [('size', w.DWORD), ('rect', w.RECT), ('work', w.RECT), ('flags', w.DWORD), ('device', w.WCHAR * 32)]

def monitors():
    found = []
    callback_type = ctypes.WINFUNCTYPE(w.BOOL, w.HMONITOR, w.HDC, ctypes.POINTER(w.RECT), w.LPARAM)
    @callback_type
    def visit(handle, dc, rectangle, data):
        info = MonitorInfo(); info.size = ctypes.sizeof(info)
        u.GetMonitorInfoW(handle, ctypes.byref(info))
        dx, dy = w.UINT(), w.UINT()
        ctypes.windll.shcore.GetDpiForMonitor(handle, 0, ctypes.byref(dx), ctypes.byref(dy))
        found.append(dict(device=info.device, primary=bool(info.flags & 1),
                          bounds=[info.rect.left, info.rect.top, info.rect.right, info.rect.bottom],
                          work=[info.work.left, info.work.top, info.work.right, info.work.bottom], dpi=dx.value))
        return True
    u.EnumDisplayMonitors(0, 0, visit, 0)
    return found

if __name__ == '__main__':
    print(json.dumps(monitors(), ensure_ascii=False, indent=2))
