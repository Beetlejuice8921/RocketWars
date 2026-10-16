from PIL import Image, ImageDraw
import random, sys
random.seed(7)
N=64
img=Image.new('RGBA',(N,N))
d=ImageDraw.Draw(img)
# небо — ступенчатый градиент
top=(110,170,205); bot=(183,216,227)
for y in range(N):
    t=min(1,(y//4*4)/44)
    c=tuple(int(top[i]+(bot[i]-top[i])*t) for i in range(3))
    d.line([(0,y),(N,y)],fill=c+(255,))
def blob(cx,cy,parts,col=(255,255,255),shade=(214,226,234)):
    for (x,y,w,h) in parts:
        d.rectangle([cx+x,cy+y+1,cx+x+w-1,cy+y+h],fill=shade)
    for (x,y,w,h) in parts:
        d.rectangle([cx+x,cy+y,cx+x+w-1,cy+y+h-1],fill=col)
# облака
blob(3,8,[(0,2,10,3),(2,0,5,3),(7,1,4,3)])
blob(46,24,[(0,2,14,3),(3,0,6,3),(8,-1,5,4)])
# остров
G=(92,178,70); G2=(120,200,84); D=(134,92,58); D2=(110,74,46); S=(128,128,136); S2=(104,104,112)
ground=[52,51,51,50,50,50,51,51,52,52,53,53,52,51,51,50,50,51,51,52,52,52,53,53,54,54,53,52,52,51,51,51,52,52,52,53,53,53,52,52,51,51,50,50,50,51,51,52,52,52,53,53,52,52,51,51,51,52,52,53,53,53,54,54]
crater=range(44,56)
for x in range(N):
    g=ground[x]
    if x in crater: g+= [3,4,5,6,6,7,7,6,6,5,4,3][x-44]
    for y in range(g,N):
        depth=y-g
        if y>=60: c=S if (x+y)%5 else S2
        elif depth==0 and x not in crater: c=G2 if (x//3)%3==0 else G
        elif depth==1 and x not in crater: c=G
        else: c=D if (x*7+y*3)%9 else D2
        d.point((x,y),fill=c)
# травинки
for x in range(0,N,3):
    if x in crater: continue
    if random.random()<0.6: d.point((x,ground[x]-1),fill=G2)
# взрыв в кратере: вспышка + кубики земли
for (x,y,s,c) in [(49,47,5,(255,240,150)),(45,45,4,(255,190,70)),(53,44,4,(255,170,60)),(48,42,3,(255,120,40)),(42,41,2,D),(57,40,2,D),(40,46,2,D2),(59,45,2,G),(51,38,2,D2),(45,37,1,D),(57,36,1,G)]:
    d.rectangle([x,y,x+s-1,y+s-1],fill=c)
# дымовой след из белых кубиков (снизу слева к ракете)
for (x,y,s) in [(4,52,5),(8,48,5),(12,45,4),(15,42,4),(18,40,3),(21,37,3),(24,36,2)]:
    d.rectangle([x,y+1,x+s-1,y+s],fill=(200,210,220))
    d.rectangle([x,y,x+s-1,y+s-1],fill=(248,248,248))
img.save(sys.argv[1]+'/bg.png')
# ракета — вертикальный спрайт, потом поворот
R=Image.new('RGBA',(16,44),(0,0,0,0)); r=ImageDraw.Draw(R)
W=(236,236,240); Wsh=(186,188,198); RED=(214,52,52); RDK=(150,30,36); DK=(40,44,56)
# корпус x 4..11
for y in range(8,34):
    for x in range(4,12):
        r.point((x,y),fill=Wsh if x>=9 else W)
# нос
for i,(a,b) in enumerate([(7,8),(6,9),(6,9),(5,10),(5,10),(4,11),(4,11),(4,11)]):
    for x in range(a,b+1): r.point((x,i+1),fill=RDK if x>=9 else RED)
# полосы и иллюминатор
for y in (14,15,28,29):
    for x in range(4,12): r.point((x,y),fill=RDK if x>=9 else RED)
r.rectangle([6,19,9,22],fill=DK); r.rectangle([7,20,8,21],fill=(110,190,230))
# стабилизаторы
for i in range(6):
    r.rectangle([3-i//2,28+i,3,28+i],fill=RED)
    r.rectangle([12,28+i,12+i//2,28+i],fill=RDK)
r.rectangle([5,34,10,35],fill=DK)
# пламя
for y,(a,b,c) in enumerate([(5,10,(255,250,200)),(5,10,(255,230,120)),(6,9,(255,200,60)),(6,9,(255,160,40)),(7,8,(255,120,30)),(7,8,(230,80,30))]):
    r.rectangle([a,36+y,b,36+y],fill=c)
R=R.resize((32,88),Image.NEAREST).rotate(-45,resample=Image.NEAREST,expand=True)
# уменьшить обратно с сохранением пикселей (NEAREST) — поворот на 2x даёт более чистую лесенку
R=R.resize((R.width//2,R.height//2),Image.NEAREST)
img.alpha_composite(R,(int(sys.argv[2]),int(sys.argv[3])))
# тёмная пиксельная рамка-обводка вокруг ракеты
img=img.resize((512,512),Image.NEAREST)
img.save(sys.argv[1]+'/icon.png')
print(R.size)
