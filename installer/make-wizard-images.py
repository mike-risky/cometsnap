import random, sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter
root = sys.argv[1]
logo = Image.open(f"{root}/assets/png/cometsnap-512.png").convert("RGBA")
font = f"{root}/assets/fonts/BungeeShade-Regular.ttf"
VOID=(7,17,58); VIOLET=(43,28,114); NAVY=(34,52,138); WHITE=(255,255,255); SUN=(255,214,10); DUST=(169,182,240)

def sky(w,h,seed=7):
    img = Image.new("RGB",(w,h))
    px = img.load()
    for y in range(h):
        t = y/(h-1)
        # violet top -> void bottom
        c = tuple(int(VIOLET[i]*(1-t)+VOID[i]*t) for i in range(3))
        for x in range(w): px[x,y]=c
    glow = Image.new("RGBA",(w,h),(0,0,0,0)); g=ImageDraw.Draw(glow)
    g.ellipse((-w*0.3,h*0.15,w*1.3,h*0.7),fill=NAVY+(90,))
    glow = glow.filter(ImageFilter.GaussianBlur(w/6))
    img = Image.alpha_composite(img.convert("RGBA"),glow)
    d = ImageDraw.Draw(img); rng=random.Random(seed)
    s = w/164
    s = w/164
    for _ in range(int(w*h/(1300*s*s))):
        x,y = rng.random()*w, rng.random()*h
        r = (0.35+rng.random()*0.75)*s*(1.6 if rng.random()<0.08 else 1)
        col = rng.choice([WHITE,WHITE,DUST,SUN]) + (int(110+rng.random()*145),)
        d.ellipse((x-r,y-r,x+r,y+r),fill=col)
    return img

def big(w,h):
    img = sky(w,h); s=w/164
    L = int(110*s); lg = logo.resize((L,L),Image.LANCZOS)
    img.alpha_composite(lg,((w-L)//2,int(h*0.20)))
    d = ImageDraw.Draw(img); f = ImageFont.truetype(font,int(26*s))
    for i,(txt,col) in enumerate([("COMET",WHITE),("SNAP",SUN)]):
        tw = d.textlength(txt,font=f)
        d.text(((w-tw)/2,int(h*0.20)+L+int(10*s)+i*int(30*s)),txt,font=f,fill=col)
    return img.convert("RGB")

def small(n):
    img = sky(n,n,3)
    L=int(n*0.86); lg=logo.resize((L,L),Image.LANCZOS)
    img.alpha_composite(lg,((n-L)//2,(n-L)//2))
    return img.convert("RGB")

out=f"{root}/assets/installer"
big(164,314).save(f"{out}/wizard-100.bmp"); big(410,785).save(f"{out}/wizard-250.bmp")
small(55).save(f"{out}/small-100.bmp"); small(138).save(f"{out}/small-250.bmp")
big(410,785).save(sys.argv[2])
