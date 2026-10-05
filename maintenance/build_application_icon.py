"""Rasterize the repository's editable K butterfly geometry into a Windows multi-size ICO."""
from pathlib import Path
import xml.etree.ElementTree as ET
from PIL import Image,ImageDraw,ImageFilter

ROOT=Path(__file__).resolve().parents[1]
source=ROOT/'Karolina.Desktop/Web/themes/karolina/assets/k-butterfly.svg'
svg=ET.fromstring(source.read_text(encoding='utf-8'))
size=1024;scale=4.4;ox=185;oy=125
base=Image.new('RGBA',(size,size));draw=ImageDraw.Draw(base)
draw.rounded_rectangle((35,35,989,989),radius=205,fill='#0c132b',outline='#535382',width=9)
gradient=Image.new('RGBA',(size,size));pixels=gradient.load()
stops=[(85,220,231),(164,138,247),(237,127,206)]
for x in range(size):
    t=x/(size-1)*2;index=min(1,int(t));blend=t-index
    color=tuple(round(a*(1-blend)+b*blend) for a,b in zip(stops[index],stops[index+1]))+(255,)
    for y in range(size):pixels[x,y]=color
glow=Image.new('RGBA',(size,size));glow_draw=ImageDraw.Draw(glow)
for polygon in svg.findall('.//{http://www.w3.org/2000/svg}polygon'):
    points=[tuple(map(float,point.split(','))) for point in polygon.attrib['points'].split()]
    points=[(round(ox+x*scale),round(oy+y*scale)) for x,y in points]
    alpha=int(255*max(.3,float(polygon.attrib.get('fill-opacity','.4'))))
    mask=Image.new('L',(size,size));ImageDraw.Draw(mask).polygon(points,fill=alpha)
    surface=gradient.copy();surface.putalpha(mask);base.alpha_composite(surface)
    draw=ImageDraw.Draw(base);draw.line(points+[points[0]],fill=(172,213,255,200),width=6)
    glow_draw.line(points+[points[0]],fill=(112,207,255,125),width=8)
base.alpha_composite(glow.filter(ImageFilter.GaussianBlur(9)))
target=ROOT/'Karolina.Desktop/Resources/karolina.ico';target.parent.mkdir(parents=True,exist_ok=True)
base.save(target,format='ICO',sizes=[(16,16),(20,20),(24,24),(32,32),(40,40),(48,48),(64,64),(128,128),(256,256)])
print('Saved',target)
