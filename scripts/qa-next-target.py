"""Local scroll/OCR/window test fixture; no external data."""
import ctypes,json,sys,tkinter as tk
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont,ImageTk
ctypes.windll.user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
root=tk.Tk();root.title('SnapRail v0.5 QA');root.geometry('1500x1000+350+500')
page=Image.new('RGB',(1500,6000));pixels=page.load()
for y in range(6000):
    for x in range(1500):pixels[x,y]=(220+(x*7+y*3)%30,220+(x*3+y*7)%30,220+(x+y*11)%30)
draw=ImageDraw.Draw(page);font=ImageFont.truetype(r'C:\Windows\Fonts\msyh.ttc',36)
for y in range(180,6000,180):
    draw.rectangle((80,y-10,1300,y+70),fill='white')
    draw.text((100,y),f'SnapRail OCR 2026  —  Row {y}',font=font,fill='#152d35')
    draw.rectangle((20,y,55,y+90),fill=(y%255,(y//3)%255,(y//7)%255))
photo=ImageTk.PhotoImage(page);canvas=tk.Canvas(root,highlightthickness=0,yscrollincrement=20);canvas.pack(fill='both',expand=True)
canvas.create_image(0,0,anchor='nw',image=photo);canvas.configure(scrollregion=(0,0,1500,6000))
def wheel(event):
    canvas.yview_scroll(-event.delta//120*3,'units')
    Path(sys.argv[1]).with_suffix('.wheel.json').write_text(json.dumps({'delta':event.delta,'view':canvas.yview()}))
root.bind_all('<MouseWheel>',wheel)
canvas.focus_set()
root.update();ctypes.windll.user32.SetForegroundWindow(ctypes.c_void_p(root.winfo_id()))
page.save(Path(sys.argv[1]).with_suffix('.png'))
Path(sys.argv[1]).write_text(json.dumps({'x':canvas.winfo_rootx(),'y':canvas.winfo_rooty(),'width':canvas.winfo_width(),'height':canvas.winfo_height()}))
root.mainloop()
