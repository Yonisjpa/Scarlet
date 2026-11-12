# Sonidos ORIGINALES (sintetizados desde cero) para los logros con Lúmina (beta 1.7.2): raro (6.0 s) y juego al 100 % (7.8 s).
# Uso: python tools/sonidos-logros-lumina.py  → logro_raro_lumina.wav y completo100_lumina.wav;
# luego: ffmpeg -i X.wav -c:a libvorbis -q:a 6 sounds/achrare_l.ogg (y ach100_l.ogg).
import sys
import os
src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'sonidos-logros.py'), encoding='utf-8').read().split('# ---------- 1)')[0]
exec(src)
N.update({"A2": 110.0, "F#3": 185.0, "G3": 196.0, "B3": 246.94, "G4": 392.0, "A6": 1760.0, "D7": 2349.32, "F#6": 1479.98, "E6": 1318.51, "B5": 987.77, "G5": 783.99, "E5": 659.25, "D2": 73.42})

def crash(d=2.6, dec=.7, hp=3500):   # platillo: ruido agudo que se apaga
    t, e = env(d, .001, dec); nz = rng.standard_normal(len(t))
    return sg.lfilter(*sg.butter(2, hp / (sr / 2), 'high'), nz) * e
def punch(f0=170, f1=55, d=.45):     # golpe seco en el pecho (medio-grave con saturación) + clic
    t, e = env(d, .001, .11); f = f1 + (f0 - f1) * np.exp(-t / .03)
    x = np.tanh(np.sin(2 * np.pi * np.cumsum(f) / sr) * 4) * e
    ck = sg.lfilter(*sg.butter(2, [1500 / (sr / 2), 6000 / (sr / 2)], 'band'), rng.standard_normal(len(t))) * np.exp(-t / .006)
    return x + ck * .5
def brass(f, d, a=.03, rel=.5, bright=3800, vib=5.2):   # metal: sierra con el filtro que se abre y un poco de vibrato
    t = T(d); v = 1 + .004 * np.sin(2 * np.pi * vib * t) * np.minimum(1, t / .4)
    x = np.zeros(len(t))
    for dv in (.994, 1.0, 1.006):
        ph = np.cumsum(f * dv * v) / sr; x += 2 * (ph % 1) - 1
    x /= 3; out = np.zeros_like(x); blk = 600
    for k in range(0, len(t), blk):
        tt = k / sr; c = 500 + (bright - 500) * (1 - np.exp(-tt / .05)) * np.exp(-tt / (d * 1.2)) + 300
        b_, a_ = sg.butter(2, min(.95, c / (sr / 2))); out[k:k+blk] = sg.lfilter(b_, a_, x[k:k+blk])
    return out * np.minimum(1, t / a) * np.minimum(1, (d - t) / rel)
def glide(f0, f1, d, a=.2):          # tono que sube (tensión)
    t = T(d); f = f0 * (f1 / f0) ** (t / d) ** 1.6; x = 2 * ((np.cumsum(f) / sr) % 1) - 1
    x = sg.lfilter(*sg.butter(2, 1600 / (sr / 2)), x); return x * np.minimum(1, t / a) * (t / d) ** 1.5
def tick(f):                          # tic del contador
    t, e = env(.09, .001, .018); return np.sin(2 * np.pi * f * t) * e

# ---------- LOGRO RARO (6.0 s): cae el diamante (0.45), cuenta regresiva del porcentaje, y el golpe grande (2.4) ----------
m = Mix(6.0); h1, h2 = .45, 2.40
m.add(noise_sweep(.42, 400, 7000, shape=2.4), h1 - .42, g=.16)                 # aire antes del golpe
m.add(boom(110, 38, 1.8, .42, .06, .8), h1, g=.95); m.add(punch(), h1, g=.5)
for nt, p, g in (("D6", -.2, .2), ("A6", .25, .15), ("D7", 0, .08)): m.add(bell(N[nt], 2.2, .55, 1.3), h1 + .005, p, g)   # cristal
m.add(crash(.9, .16, 5000), h1, 0, .1)
m.add(rumble(1.9, 38, .25, .25), h1 + .1, 0, .28)
# el porcentaje baja: tics que suben de tono y se aprietan (0.6 → 2.2 s)
tt = .62; k = 0; scale = [N["D5"], N["E5"], N["F#5"], N["A5"], N["B5"], N["D6"], N["E6"], N["F#6"], N["A6"]]
while tt < 2.22:
    m.add(tick(scale[min(len(scale) - 1, k // 3)]), tt, (-1) ** k * .35, .05 + .05 * (tt - .6) / 1.6); tt += max(.035, .16 - k * .0065); k += 1
m.add(glide(N["D3"], N["D4"], 1.7), h2 - 1.72, 0, .18); m.add(noise_sweep(1.1, 300, 9000, shape=2.8), h2 - 1.1, g=.24)
m.add(blip(200, 900, .2), h2 - .1, 0, .16)
# golpe 2: se revela el logro
m.add(boom(95, 28, 3.4, .95, .09, .9), h2, g=1.0); m.add(punch(190, 50, .5), h2, g=.6)
m.add(crash(2.8, .75, 4200), h2, 0, .2)
for k2, (nt, p) in enumerate([("D4", -.3), ("A4", .3), ("D5", -.2), ("F#5", .2), ("A5", -.1), ("D6", .1)]): m.add(bell(N[nt], 3.4, 1.25, 1.2), h2 + k2 * .012, p, .13)
m.add(sum(brass(N[x], 1.5, .02, .9, 3200) for x in ("D3", "A3", "D4", "F#4")), h2, 0, .15)
m.add(sum(supersaw(N[x], 3.4, 250, 2400, a=.04, rel=2.4, det=.018) for x in ("D2", "D3", "A3", "D4")), h2, 0, .16)
for k2, nt in enumerate(["A5", "D6", "F#6", "A6", "D7"]): m.add(bell(N[nt], 1.4, .35, .9), h2 + .14 + k2 * .085, -.6 + k2 * .3, .07)   # arpegio de cristal
for k2 in range(9): m.add(bell(rng.choice([N["A6"], N["D7"], N["F#6"]]), .9, .2, .5), h2 + .75 + k2 * .2 + rng.uniform(0, .06), rng.uniform(-.8, .8), .04)
m.add(rumble(3.2, 34, .2, 2.2), h2, 0, .32)
m.out("logro_raro_lumina.wav", verb=.45, size=.9, peak=.89)

# ---------- JUEGO AL 100 % (7.8 s): tambores que aprietan mientras sube el contador, el golpe (2.6) y la fanfarria ----------
m = Mix(7.8); hit = 2.60
m.add(rumble(2.5, 32, 2.0, .05), hit - 2.5, 0, .5)
m.add(noise_sweep(2.3, 150, 10000, bw=1.0, shape=2.8), hit - 2.3, g=.3)
m.add(glide(N["D2"], N["D4"], 2.2), hit - 2.2, 0, .22); m.add(glide(N["A2"], N["A4"], 2.2), hit - 2.2, 0, .12)
for k, tt in enumerate([.50, 1.00, 1.40, 1.70, 1.94, 2.12, 2.26, 2.36, 2.44, 2.51, 2.56]):                    # timbales cada vez más juntos
    g = .35 + .5 * k / 10; m.add(boom(100, 46, .7, .16, .05, .7), tt, (-1) ** k * .15, g); m.add(punch(150, 60, .3), tt, 0, g * .35)
tt = .55; k = 0
while tt < hit - .03:                                                                                          # el contador 0 → 100
    m.add(tick(N["D5"] * 2 ** (min(24, k) / 24)), tt, (-1) ** k * .4, .035 + .05 * tt / hit); tt += max(.03, .13 - k * .004); k += 1
# EL GOLPE
m.add(boom(125, 26, 4.8, 1.25, .1, 1.0), hit, g=1.0); m.add(punch(210, 48, .6), hit, g=.75)
t, e = env(1.8, .002, .45); m.add(np.sin(2 * np.pi * 73 * t + 1.8 * np.sin(2 * np.pi * 110 * t) * e) * e, hit, 0, .45)
m.add(crash(3.6, 1.0, 3200), hit, 0, .3); m.add(crash(1.2, .2, 7000), hit, 0, .18)
m.add(sum(brass(N[x], 2.2, .015, 1.2, 4200) for x in ("D3", "A3", "D4", "F#4", "A4")), hit, 0, .2)           # acorde de metales
m.add(sum(supersaw(N[x], 4.8, 300, 3400, a=.04, rel=2.8, det=.016) for x in ("D2", "D3", "A3", "D4", "F#4", "A4")), hit, 0, .15)
for k, (nt, p) in enumerate([("D4", -.3), ("A4", .3), ("D5", -.2), ("F#5", .2), ("A5", -.4), ("D6", .4)]): m.add(bell(N[nt], 4.6, 1.6, 1.2), hit + k * .012, p, .12)
# fanfarria: re – la – re – fa# (triunfal), con metales y campana a la vez
for nt, tt, d in (("D5", 0.0, .5), ("A5", .26, .5), ("D6", .52, .6), ("F#6", .86, 2.2)):
    m.add(brass(N[nt] / 2, d + .25, .012, .25, 5200), hit + tt, 0, .2); m.add(brass(N[nt], d + .25, .012, .25, 4200), hit + tt, .15, .1); m.add(bell(N[nt], 2.2, .7, 1.0), hit + tt, -.15, .1)
m.add(boom(100, 30, 2.6, .8, .08, .9), hit + .86, g=.7); m.add(crash(2.4, .7, 4000), hit + .86, 0, .16)       # segundo golpe, con la última nota
m.add(sum(brass(N[x], 2.6, .02, 1.6, 3600) for x in ("D3", "A3", "D4", "F#4", "A4", "D5")), hit + .86, 0, .14)
m.add(rumble(4.6, 30, .2, 2.8), hit, 0, .4)
for k in range(16): m.add(bell(rng.choice([N["D6"], N["F#6"], N["A6"], N["D7"]]), 1.0, .22, .6), hit + 1.3 + k * .19 + rng.uniform(0, .07), rng.uniform(-.8, .8), .04)   # chispas
t = T(3.6); pe = np.minimum(1, t / .5) * np.minimum(1, (3.6 - t) / 2.0)
m.add(sum(np.sin(2 * np.pi * N[x] * t + .7 * np.sin(2 * np.pi * .25 * t)) for x in ("D5", "A5", "D6")) * pe, hit + 1.0, 0, .028)
m.add(noise_sweep(.5, 4000, 300, rise=False), 7.15, 0, .1)                                                     # se va
m.out("completo100_lumina.wav", verb=.5, size=1.05, peak=.89)
print("ok")
