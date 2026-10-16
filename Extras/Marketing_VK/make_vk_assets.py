# Графика для VK Игр из материалов Яндекса (../Marketing): иконки, фавикон, большой сниппет, скриншоты.
# Запуск: python make_vk_assets.py  (из папки Marketing_VK). Требования VK — в README.md.
import os, shutil
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, '..', 'Marketing')
TEMPLATE = os.path.join(HERE, '..', 'RocketWars', 'Assets', 'WebGLTemplates', 'RocketWars-VK')
os.chdir(HERE)

# --- иконки: исходник 512 = пиксель-арт 64×64, увеличенный в 8 раз
base = Image.open(os.path.join(SRC, 'icon_512.png')).convert('RGB').resize((64, 64), Image.NEAREST)
base.resize((576, 576), Image.NEAREST).save('icon_576.png')          # универсальная (ровно ×9)
base.resize((278, 278), Image.NEAREST).save('icon_278.png')          # каталог и сниппеты
base.resize((150, 150), Image.NEAREST).save('icon_150.png')          # маленькая
fav = base.resize((32, 32), Image.BOX)                                # фавикон: усреднение 2×2 — без потери ракеты
fav.save('favicon_32.png', optimize=True)
shutil.copy('favicon_32.png', os.path.join(TEMPLATE, 'favicon.png'))  # шаблон кладёт его рядом с index.html

# --- большой сниппет 1120×630: обложка Яндекса (make_cover.py) в масштабе ×7, 160×90 логических пикселей
src = open(os.path.join(SRC, 'make_cover.py'), encoding='utf-8').read()
for a, b in [
    ('W,H,K=160,94,5', 'W,H,K=160,94,7'),
    ('S=7\n', 'S=8\n'),                                               # заголовок чуть крупнее; ниже — солдат на ракете
    ('img.alpha_composite(T,((W*K-T.width)//2,22))', 'img.alpha_composite(T,((W*K-T.width)//2,42))'),
    # 94 → 90 строк: срезаем 4 строки пустого неба сверху; низ кадра — земля (VK кладёт туда свой текст)
    ('img.save("cover.png"); print(img.size)', 'img=img.crop((0,4*K,W*K,94*K)); img.convert("RGB").save("snippet_1120x630.png", optimize=True); print(img.size)'),
]:
    assert a in src, a
    src = src.replace(a, b)
exec(compile(src, 'make_cover.py', 'exec'), {})

# --- скриншоты: горизонтальные 1200×600 (2:1) — центр кадра 1920×960 из скриншотов 1920×1080
os.makedirs('screenshots', exist_ok=True)
for name in sorted(os.listdir(os.path.join(SRC, 'screenshots'))):
    if not name.endswith('.png'):
        continue
    im = Image.open(os.path.join(SRC, 'screenshots', name)).convert('RGB')
    w, h = im.size
    ch = w // 2
    top = (h - ch) // 2
    im = im.crop((0, top, w, top + ch)).resize((1200, 600), Image.LANCZOS)
    im.save(os.path.join('screenshots', name[:-4] + '.jpg'), quality=92)

for f in ['icon_576.png', 'icon_278.png', 'icon_150.png', 'favicon_32.png', 'snippet_1120x630.png']:
    print(f, Image.open(f).size, os.path.getsize(f), 'bytes')
