from PIL import Image, ImageDraw, ImageFont
import random, math
random.seed(3)
W,H,K=160,94,5
FONT="D:/ClaudeRocketWars/RocketWars/Assets/_Project/Resources/Fonts/PressStart2P-Regular.ttf"
img=Image.new('RGBA',(W,H)); d=ImageDraw.Draw(img)
top=(96,160,200); bot=(190,222,232)
for y in range(H):
    t=min(1,(y//4*4)/70); d.line([(0,y),(W,y)],fill=tuple(int(top[i]+(bot[i]-top[i])*t) for i in range(3)))
def blob(cx,cy,parts,col=(255,255,255),shade=(212,226,234)):
    for (x,y,w,h) in parts: d.rectangle([cx+x,cy+y+1,cx+x+w-1,cy+y+h],fill=shade)
    for (x,y,w,h) in parts: d.rectangle([cx+x,cy+y,cx+x+w-1,cy+y+h-1],fill=col)
blob(4,30,[(0,2,12,3),(3,0,6,3),(8,1,5,3)])
blob(128,26,[(0,2,16,3),(4,0,7,3),(10,-1,5,4)])
blob(70,44,[(0,2,14,3),(3,0,6,3)])
blob(40,70,[(0,2,10,2),(2,0,5,3)],(240,246,250),(214,226,234))
G=(92,178,70); G2=(124,204,86); D=(134,92,58); D2=(108,72,44); S=(124,124,134); S2=(98,98,110)
def island(x0,x1,ytop,seed,crater=None):
    rnd=random.Random(seed)
    for x in range(x0,x1):
        g=ytop+rnd.choice([0,0,0,1,-1])
        if crater and crater[0]<=x<crater[1]:
            m=(crater[0]+crater[1])/2; g+=int(5-abs(x-m)*0.9) if abs(x-m)<5.5 else 0
        edge=min(x-x0,x1-1-x)
        bottom=ytop+8+min(edge,14)//2+rnd.choice([0,1])
        for y in range(g,bottom+1):
            dep=y-g
            if dep==0 and not (crater and crater[0]<=x<crater[1]): c=G2 if (x//3)%3==0 else G
            elif dep==1: c=G
            elif y>ytop+6: c=S if (x+y)%5 else S2
            else: c=D if (x*7+y*3)%9 else D2
            d.point((x,y),fill=c)
        if rnd.random()<0.5 and not (crater and crater[0]<=x<crater[1]): d.point((x,g-1),fill=G2)
island(0,52,74,1)
island(108,160,74,2,crater=(118,130))
def silo(x,y,team):
    c=(200,50,50) if team==0 else (60,170,70); cd=(140,30,34) if team==0 else (36,120,48)
    d.rectangle([x,y-12,x+6,y],fill=c); d.rectangle([x+5,y-12,x+6,y],fill=cd)
    for yy in (y-9,y-4): d.rectangle([x,yy,x+6,yy],fill=(70,72,84))
    d.rectangle([x-1,y-13,x+7,y-13],fill=(150,152,160))
    d.rectangle([x-2,y+1,x+8,y+1],fill=(160,160,166))
    # нос ракеты из шахты
    d.rectangle([x+2,y-16,x+4,y-14],fill=(236,236,240)); d.point((x+3,y-17),fill=c)
silo(8,73,0); silo(30,73,0); silo(138,73,1); silo(150,73,1)
# взрыв в кратере справа
for (x,y,s,c) in [(122,70,6,(255,240,150)),(117,68,5,(255,190,70)),(127,66,5,(255,170,60)),(121,63,4,(255,120,40)),(114,62,2,D),(131,61,2,D),(112,68,2,D2),(133,67,2,G),(124,58,2,D2),(117,57,1,D),(130,56,1,G),(110,60,1,D)]:
    d.rectangle([x,y,x+s-1,y+s-1],fill=c)
# траектории: дуги из кубиков дыма
def arc(p0,p1,h,n,t0,t1):
    pts=[]
    for i in range(n):
        t=t0+(t1-t0)*i/(n-1)
        x=p0[0]+(p1[0]-p0[0])*t; y=p0[1]+(p1[1]-p0[1])*t-4*h*t*(1-t)
        pts.append((x,y,t))
    return pts
def smoke(pts):
    for i,(x,y,t) in enumerate(pts):
        s=2 if i>len(pts)-3 else 3
        x,y=int(x),int(y)
        d.rectangle([x,y+1,x+s-1,y+s],fill=(196,208,220)); d.rectangle([x,y,x+s-1,y+s-1],fill=(248,248,248))
red_path=((33,56),(124,64),32); green_path=((141,56),(12,64),16)
smoke(arc(*red_path,12,0.03,0.29))
smoke(arc(*green_path,9,0.03,0.2))
def rocket(team, rider=False):
    R=Image.new('RGBA',(16,46),(0,0,0,0)); r=ImageDraw.Draw(R)
    Wc=(236,236,240); Ws=(186,188,198); C=(214,52,52) if team==0 else (64,176,74); Cd=(150,30,36) if team==0 else (38,120,50); DK=(40,44,56)
    for y in range(10,36):
        for x in range(4,12): r.point((x,y),fill=Ws if x>=9 else Wc)
    for i,(a,b) in enumerate([(7,8),(6,9),(6,9),(5,10),(5,10),(4,11),(4,11),(4,11),(4,11)]):
        for x in range(a,b+1): r.point((x,i+1),fill=Cd if x>=9 else C)
    for y in (15,16,30,31):
        for x in range(4,12): r.point((x,y),fill=Cd if x>=9 else C)
    r.rectangle([6,21,9,24],fill=DK); r.rectangle([7,22,8,23],fill=(110,190,230))
    for i in range(6):
        r.rectangle([3-i//2,30+i,3,30+i],fill=C); r.rectangle([12,30+i,12+i//2,30+i],fill=Cd)
    r.rectangle([5,36,10,37],fill=DK)
    for y,(a,b,c) in enumerate([(5,10,(255,250,200)),(5,10,(255,230,120)),(6,9,(255,200,60)),(6,9,(255,160,40)),(7,8,(255,120,30)),(7,8,(230,80,30))]):
        r.rectangle([a,38+y,b,38+y],fill=c)
    return R
def place(R,x,y,angle):
    R2=R.resize((R.width*2,R.height*2),Image.NEAREST).rotate(angle,resample=Image.NEAREST,expand=True)
    R2=R2.resize((R2.width//2,R2.height//2),Image.NEAREST)
    img.alpha_composite(R2,(int(x-R2.width/2),int(y-R2.height/2)))
# направление касательной в конце дыма
def tangent(p0,p1,h,t):
    dx=p1[0]-p0[0]; dy=(p1[1]-p0[1])-4*h*(1-2*t)
    return math.degrees(math.atan2(dx,-dy))  # 0 = вверх
pr=arc(*red_path,2,0.42,0.42)[0]; ar=tangent(*red_path,0.42)
pg=arc(*green_path,2,0.33,0.33)[0]; ag=tangent(*green_path,0.33)
place(rocket(0),pr[0],pr[1],-ar)
place(rocket(1),pg[0],pg[1],-ag)
# солдат верхом на красной ракете (каска, тело) — у центра ракеты, сверху
sx,sy=int(pr[0])-3,int(pr[1])-9
d.rectangle([sx,sy+3,sx+3,sy+6],fill=(70,96,60))      # тело в камуфляже
d.rectangle([sx+1,sy+1,sx+3,sy+2],fill=(232,190,150)) # лицо
d.rectangle([sx,sy-1,sx+4,sy],fill=(90,110,70))       # каска
d.rectangle([sx,sy,sx+4,sy],fill=(200,50,50))         # полоса команды
d.rectangle([sx+4,sy+3,sx+6,sy+3],fill=(232,190,150)) # рука вперёд
img=img.resize((W*K,H*K),Image.NEAREST)
# заголовок — отдельный слой с пикселем 7 px (крупнее мира), Press Start 2P 8px
f=ImageFont.truetype(FONT,8)
text="ROCKET WARS"
T=Image.new('RGBA',(100,14),(0,0,0,0)); t=ImageDraw.Draw(T)
for dx,dy in [(-1,0),(1,0),(0,-1),(0,1),(-1,-1),(1,1),(-1,1),(1,-1),(0,2),(1,2),(-1,2)]:
    t.text((6+dx,2+dy),text,font=f,fill=(30,30,46))
t.text((6,2),text,font=f,fill=(255,214,72))
# блик сверху букв
px=T.load()
for x in range(T.width):
    for y in range(T.height-1):
        if px[x,y][:3]==(255,214,72) and px[x,y-1][3]>0 and px[x,y-1][:3]==(30,30,46): px[x,y]=(255,240,160,255)
T=T.crop(T.getbbox()); S=7
T=T.resize((T.width*S,T.height*S),Image.NEAREST)
img.alpha_composite(T,((W*K-T.width)//2,22))
img.save("cover.png"); print(img.size)
