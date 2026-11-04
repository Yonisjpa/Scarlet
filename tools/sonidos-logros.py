# Sonidos ORIGINALES de logros para Scarlet (sintetizados desde cero; no usan audio de Xbox ni de nadie).
# Uso: python tools/sonidos-logros.py  (deja logro_normal.wav, logro_raro.wav y completo100.wav en la carpeta actual;
# luego se pasan a sounds/achieve.ogg, achrare.ogg y ach100.ogg con: ffmpeg -i X.wav -c:a libvorbis -q:a 6 Y.ogg)
import numpy as np, scipy.signal as sg, scipy.io.wavfile as wf
sr = 48000
rng = np.random.default_rng(11)
class Mix:
    def __init__(s, dur): s.n = int(dur * sr); s.L = np.zeros(s.n); s.R = np.zeros(s.n)
    def add(s, sig, t0, pan=0.0, g=1.0):
        i = int(t0 * sr); m = min(len(sig), s.n - i)
        if m <= 0: return
        s.L[i:i+m] += sig[:m] * g * np.sqrt((1 - pan) / 2) * 1.414; s.R[i:i+m] += sig[:m] * g * np.sqrt((1 + pan) / 2) * 1.414
    def out(s, name, verb=.4, size=.7, peak=.9, drive=1.15):
        ir_t = np.arange(int(size * 5 * sr)) / sr
        for ch, seed in ((0, 1), (1, 2)):
            r = np.random.default_rng(seed); ir = r.standard_normal(len(ir_t)) * np.exp(-ir_t / size)
            ir = sg.lfilter(*sg.butter(1, 6000 / (sr / 2)), ir); ir /= np.sqrt((ir ** 2).sum())
            x = s.L if ch == 0 else s.R; wet = sg.fftconvolve(x, ir)[:s.n]
            if ch == 0: s.L = x + verb * wet
            else: s.R = x + verb * wet
        st = np.stack([s.L, s.R], 1)
        st = np.tanh(st / np.abs(st).max() * drive); st *= peak / np.abs(st).max()   # un poco de saturación: más cuerpo
        f = int(.25 * sr); st[-f:] *= np.linspace(1, 0, f)[:, None]
        wf.write(name, sr, (st * 32767).astype(np.int16))
def T(d): return np.arange(int(d * sr)) / sr
def env(d, a, dec, rel=None):
    t = T(d); e = np.minimum(1, t / max(a, 1e-4)) * np.exp(-t / dec)
    if rel: e *= np.minimum(1, (d - t) / rel)
    return t, e
def bell(f, d=3.0, dec=1.2, bright=1.0):   # campana FM (vidrio/metal)
    t, e = env(d, .002, dec); mi = bright * 2.2 * np.exp(-t / (dec * .35))
    return np.sin(2 * np.pi * f * t + mi * np.sin(2 * np.pi * f * 3.5 * t)) * e
def supersaw(f, d, cut0, cut1, a=.6, rel=.8, voices=7, det=.012):
    t = T(d); x = np.zeros(len(t))
    for v in range(voices):
        dv = 1 + det * (v - (voices - 1) / 2) / ((voices - 1) / 2); ph = rng.uniform(0, 1)
        x += 2 * ((f * dv * t + ph) % 1) - 1
    x /= voices; out = np.zeros_like(x); blk = 1200
    for k in range(0, len(t), blk):
        c = cut0 + (cut1 - cut0) * min(1, k / len(t) * 1.4); b, a_ = sg.butter(2, min(.95, c / (sr / 2)))
        out[k:k+blk] = sg.lfilter(b, a_, x[k:k+blk])
    e = np.minimum(1, t / a) * np.minimum(1, (d - t) / rel); return out * e
def sub(f0=95, f1=36, d=1.6, dec=.45, drop=.07):
    t, e = env(d, .002, dec); f = f1 + (f0 - f1) * np.exp(-t / drop)
    x = np.sin(2 * np.pi * np.cumsum(f) / sr); return np.tanh(x * 2.2) * e
def noise_sweep(d, f0, f1, bw=1.2, shape=2.0, rise=True):
    t = T(d); x = t / t[-1]; nz = rng.standard_normal(len(t)); out = np.zeros_like(nz); blk = 1200
    for k in range(0, len(t), blk):
        fc = f0 * (f1 / f0) ** x[k]; lo, hi = fc / (1 + bw / 2), min(sr * .45, fc * (1 + bw / 2)); b, a = sg.butter(2, [lo / (sr / 2), hi / (sr / 2)], 'band')
        out[k:k+blk] = sg.lfilter(b, a, nz[k:k+blk])
    return out * ((x ** shape) if rise else np.sin(np.pi * x))
def blip(f0, f1, d=.25):   # «bloop» redondo que sube
    t, e = env(d, .003, d / 3); f = f0 * (f1 / f0) ** np.minimum(1, t / .06)
    return np.sin(2 * np.pi * np.cumsum(f) / sr) * e
N = {"D2":73.42,"D3":146.83,"A3":220,"D4":293.66,"E4":329.63,"F#4":369.99,"A4":440,"B4":493.88,"C#5":554.37,"D5":587.33,"E5":659.25,"F#5":739.99,"A5":880,"B5":987.77,"D6":1174.66,"E6":1318.51,"F#6":1479.98,"A6":1760,"D7":2349.32}


def rumble(d, f=42, a=.3, rel=.8):   # retumbo grave continuo (ruido muy filtrado + seno)
    t = T(d); nz = sg.lfilter(*sg.butter(2, 90 / (sr / 2)), rng.standard_normal(len(t))); nz /= np.abs(nz).max()
    e = np.minimum(1, t / a) * np.minimum(1, (d - t) / rel); return (nz * .6 + np.sin(2 * np.pi * f * t) * .5) * e
def boom(f0, f1, d, dec, drop=.08, g2=.6):   # golpe grave en dos capas (cuerpo + sub-octava)
    return sub(f0, f1, d, dec, drop) + g2 * sub(f0 / 2, max(24, f1 / 2), d, dec * 1.3, drop * 1.2)

# ---------- 1) LOGRO NORMAL (~2.2 s, 25 %): golpe grave cálido + «bloop» + dos campanas ----------
m = Mix(2.8); h = .3
m.add(noise_sweep(.3, 300, 2500, shape=1.6), h - .25, g=.08)
m.add(boom(105, 44, 1.4, .32), h, g=1.0)
m.add(blip(180, 360, .3), h, g=.35)
m.add(supersaw(N["D3"], 1.4, 300, 900, a=.02, rel=1.0) + supersaw(N["A3"], 1.4, 300, 900, a=.02, rel=1.0), h, 0, .22)
m.add(bell(N["A5"], 2.0, .6, .9), h + .05, -.2, .16); m.add(bell(N["D6"], 2.2, .7, .9), h + .17, .2, .14)
m.out("logro_normal.wav", verb=.35, size=.5, peak=.89)

# ---------- 2) LOGRO RARO (~5.5 s, 50 %): dos golpes como la referencia ----------
# golpe 1 (0.25 s, aparece el disco): campana + grave; se queda un retumbo; subida corta;
# golpe 2 (1.75 s, se estira la barra): sub muy profundo + acorde grande + destellos que se apagan largo
m = Mix(6.0); h1, h2 = .25, 1.75
m.add(boom(90, 40, 1.6, .4), h1, g=.75)
m.add(bell(N["D5"], 2.6, .9, .8), h1, -.15, .22); m.add(bell(N["A5"], 2.6, .9, .8), h1 + .02, .15, .16); m.add(bell(N["D6"], 2.4, .8, .6), h1 + .03, 0, .08)
m.add(rumble(1.6, 40, .2, .3), h1 + .1, 0, .25)
m.add(noise_sweep(.7, 300, 6000, shape=2.6), h2 - .68, g=.2)
m.add(supersaw(N["D3"], .7, 150, 1400, a=.6, rel=.05, det=.02), h2 - .68, 0, .2)
m.add(boom(85, 30, 3.4, .9, .1, .8), h2, g=1.0)
t, e = env(1.4, .002, .35); m.add(np.sin(2 * np.pi * 82 * t + 1.2 * np.sin(2 * np.pi * 123 * t) * e) * e, h2, 0, .35)   # timbal
for k, (nt, p) in enumerate([("D4", -.3), ("A4", .3), ("D5", -.2), ("F#5", .2), ("A5", 0)]):
    m.add(bell(N[nt], 3.8, 1.3, 1.1), h2 + k * .015, p, .14)
m.add(sum(supersaw(N[x], 3.6, 250, 2200, a=.04, rel=2.4, det=.018) for x in ("D2", "D3", "A3", "D4")) if "D2" in N else 0, h2, 0, .2)
for k in range(8):
    m.add(bell(rng.choice([N["A6"], N["D7"], N["F#6"]]), .9, .2, .5), h2 + .6 + k * .17 + rng.uniform(0, .05), rng.uniform(-.8, .8), .05)
m.add(rumble(3.6, 36, .3, 2.2), h2, 0, .35)
m.out("logro_raro.wav", verb=.45, size=.9, peak=.89)

# ---------- 3) JUEGO AL 100 % (~7.8 s, 75 %): épico y con mucho grave ----------
m = Mix(7.8); hit = 1.95
m.add(rumble(1.9, 34, 1.5, .05), hit - 1.85, 0, .45)                       # retumbo que crece
m.add(noise_sweep(1.8, 200, 9000, bw=1.0, shape=2.6), hit - 1.8, g=.26)
for nt in ("D2", "D3", "A3", "D4"):
    m.add(supersaw(N[nt], 1.8, 120, 2800, a=1.6, rel=.05, det=.02), hit - 1.8, 0, .14)
for k in range(16):
    m.add(bell(rng.choice([N["D6"], N["E6"], N["F#6"], N["A6"]]), .5, .08, .5), .75 + k * .07 + rng.uniform(-.015, .015), rng.uniform(-.7, .7), .045)
m.add(blip(160, 700, .25), hit - .12, 0, .2)
m.add(boom(120, 28, 4.5, 1.1, .1, 1.0), hit, g=1.0)                        # el golpe: muy profundo y largo
t, e = env(1.6, .002, .4); m.add(np.sin(2 * np.pi * 73 * t + 1.6 * np.sin(2 * np.pi * 110 * t) * e) * e, hit, 0, .45)   # timbal grande
m.add(sg.lfilter(*sg.butter(2, 1800 / (sr / 2)), rng.standard_normal(int(.8 * sr))) * env(.8, .001, .12)[1], hit, 0, .35)
for k, (nt, p) in enumerate([("D4", -.3), ("A4", .3), ("D5", -.2), ("F#5", .2), ("A5", -.4), ("D6", .4)]):
    m.add(bell(N[nt], 4.8, 1.6, 1.2), hit + k * .012, p, .15)
m.add(sum(supersaw(N[x], 4.6, 300, 3200, a=.04, rel=2.6, det=.016) for x in ("D2", "D3", "A3", "D4", "F#4", "A4")), hit, 0, .17)
m.add(rumble(4.4, 30, .2, 2.6), hit, 0, .4)
m.add(boom(70, 30, 2.0, .6), hit + 1.4, g=.35)                               # segundo latido grave, más suave
for k, nt in enumerate(["D6", "F#6", "A6", "D7", "A6", "D7"]):
    m.add(bell(N[nt], 1.6, .4, .8), hit + .2 + k * .1, -.6 + k * .24, .06)
t = T(4.2); pe = np.minimum(1, t / .6) * np.minimum(1, (4.2 - t) / 1.8)
m.add(sum(np.sin(2 * np.pi * N[x] * t + .7 * np.sin(2 * np.pi * .25 * t)) for x in ("D5", "A5", "D6")) * pe, 2.3, 0, .025)
m.add(noise_sweep(.55, 4000, 300, rise=False), 6.4, 0, .14)
m.add(boom(80, 40, .9, .2), 6.9, g=.35); m.add(bell(N["A5"], 1.0, .3, .5), 6.95, 0, .08)
m.out("completo100.wav", verb=.5, size=1.0, peak=.89)
print("ok")
