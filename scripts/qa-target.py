"""Responsive desktop test target; keep its event loop separate from the input driver."""
import tkinter as tk
import ctypes

ctypes.windll.user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))

target = tk.Tk()
target.title('SnapRail annotation QA')
target.geometry('1400x1050+300+350')
target.attributes('-topmost', True)
target.after(150, target.lift)
canvas = tk.Canvas(target, width=1400, height=1050, background='#e6f2ee', highlightthickness=0)
canvas.pack(fill='both', expand=True)
canvas.create_text(350, 150, text='SnapRail / annotation test', fill='#126b64', font=('Segoe UI', 26, 'bold'))
for y in range(250, 800, 9):
    for x in range(700, 1300, 9):
        canvas.create_rectangle(x, y, x+9, y+9, outline='', fill='#126b64' if (x+y) % 2 else '#e9b56d')
target.mainloop()
