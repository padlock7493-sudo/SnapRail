"""Export the approved square icon artwork to Windows icon containers.

Pillow is required. The optional inset and corner radius specify the tile's
export boundary; pixels outside it are transparent in the Windows asset.
"""
import argparse
from pathlib import Path
from PIL import Image, ImageDraw, ImageChops

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('source', type=Path)
parser.add_argument('destination', type=Path, help='Output name without extension')
parser.add_argument('--inset', type=int, default=0)
parser.add_argument('--radius', type=int, default=0)
args = parser.parse_args()
image = Image.open(args.source).convert('RGBA')
if image.width != image.height:
    parser.error('Artwork must be square')
if args.inset:
    image = image.crop((args.inset, args.inset, image.width-args.inset, image.height-args.inset))
if args.radius:
    mask = Image.new('L', image.size)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, image.width-1, image.height-1), radius=args.radius, fill=255)
    image.putalpha(ImageChops.multiply(image.getchannel('A'), mask))
image = image.resize((944, 944), Image.Resampling.LANCZOS)
master = Image.new('RGBA', (1024, 1024))
master.paste(image, (40, 40))
args.destination.parent.mkdir(parents=True, exist_ok=True)
master.save(args.destination.with_suffix('.png'))
sizes = [(n, n) for n in (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)]
frames = []
for size in sizes:
    frame = master.resize(size, Image.Resampling.LANCZOS)
    frame.putalpha(frame.getchannel('A').point(lambda alpha: 0 if alpha < 4 else alpha))
    frames.append(frame)
master.save(args.destination.with_suffix('.ico'), sizes=sizes, append_images=frames)
with Image.open(args.destination.with_suffix('.ico')) as ico:
    assert ico.ico.sizes() == set(sizes)
    for size in sizes:
        frame = ico.ico.getimage(size).convert('RGBA')
        assert frame.getpixel((0, 0))[3] == 0
print(f'Exported PNG master and {len(sizes)} ICO sizes: {args.destination}')
