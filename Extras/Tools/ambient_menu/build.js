// Фон главного меню: исходная мелодия (base.wav, 22 кГц, моно) + новые слои.
// Ля минор, 84 BPM, 8 тактов 4/4 (ровно 22,857 с): Am F C G Am F Dm E.
// Всё считается по кругу (хвосты нот и реверба заходят в начало), поэтому петля стыкуется без щелчка.
// Запуск: node build.js [out.wav]   (по умолчанию пишет в Assets/_Project/Resources/Sounds/AmbientMenu.wav)
const fs = require('fs');
const path = require('path');

const SR = 44100;
const BPM = 84;
const BEAT = 60 / BPM;
const BAR = BEAT * 4;
const OUT = process.argv[2] || path.join(__dirname, '../../../Assets/_Project/Resources/Sounds/AmbientMenu.wav');

// Громкости слоёв
const MIX = { base: 1.0, pad: 0.12, choir: 0.045, sub: 0.10, bell: 0.06, boom: 0.12, cello: 0.09, snare: 0.16, reverbBase: 0.18, reverbLayers: 0.5 };

// Подголосок виолончели: [нота MIDI, длительность в долях], по 4 доли на такт
const CELLO = [
    [64, 3], [62, 1],   // Am
    [60, 2], [57, 2],   // F
    [55, 3], [57, 1],   // C
    [59, 2], [62, 2],   // G
    [64, 4],            // Am
    [65, 2], [60, 2],   // F
    [62, 2], [57, 2],   // Dm
    [56, 2], [59, 2],   // E
];

// Малый барабан: сила удара по шестнадцатым такта (0 — пауза); в последнем такте — дробь к повтору
const SNARE = [0, 0, 0, 0, 0.6, 0, 0, 0.25, 0, 0, 0.3, 0, 0.6, 0, 0.25, 0.25];
const SNARE_FILL = [0, 0, 0, 0, 0.6, 0, 0, 0.25, 0, 0, 0.3, 0, 0.35, 0.45, 0.55, 0.7];

// Аккорды по тактам: корень для суббаса, голоса пэда (MIDI), верх для хора, колокольчики на 1-ю и 3-ю доли
const BARS = [
    { root: 33, pad: [52, 57, 60, 64], choir: [72, 76], bell: [81, 88] }, // Am
    { root: 29, pad: [53, 57, 60, 65], choir: [72, 77], bell: [81, 84] }, // F
    { root: 36, pad: [52, 55, 60, 64], choir: [72, 76], bell: [79, 84] }, // C
    { root: 31, pad: [50, 55, 59, 62], choir: [71, 74], bell: [79, 83] }, // G
    { root: 33, pad: [52, 57, 60, 64], choir: [72, 76], bell: [84, 88] }, // Am
    { root: 29, pad: [53, 57, 60, 65], choir: [72, 77], bell: [84, 89] }, // F
    { root: 38, pad: [53, 57, 62, 65], choir: [74, 77], bell: [86, 81] }, // Dm
    { root: 28, pad: [52, 56, 59, 64], choir: [71, 76], bell: [83, 80] }, // E
];

const hz = m => 440 * Math.pow(2, (m - 69) / 12);

// ---------- WAV ----------
function readWav(file) {
    const b = fs.readFileSync(file);
    let p = 12, fmt, data;
    while (p < b.length) {
        const id = b.toString('ascii', p, p + 4), size = b.readUInt32LE(p + 4);
        if (id === 'fmt ') fmt = { ch: b.readUInt16LE(p + 10), sr: b.readUInt32LE(p + 12) };
        if (id === 'data') data = b.subarray(p + 8, p + 8 + size);
        p += 8 + size + (size & 1);
    }
    const n = data.length / 2 / fmt.ch, x = new Float32Array(n);
    for (let i = 0; i < n; i++) x[i] = data.readInt16LE(i * 2 * fmt.ch) / 32768;
    return { sr: fmt.sr, x };
}

function writeWav(file, L, R) {
    const n = L.length, b = Buffer.alloc(44 + n * 4);
    b.write('RIFF', 0); b.writeUInt32LE(36 + n * 4, 4); b.write('WAVEfmt ', 8);
    b.writeUInt32LE(16, 16); b.writeUInt16LE(1, 20); b.writeUInt16LE(2, 22);
    b.writeUInt32LE(SR, 24); b.writeUInt32LE(SR * 4, 28); b.writeUInt16LE(4, 32); b.writeUInt16LE(16, 34);
    b.write('data', 36); b.writeUInt32LE(n * 4, 40);
    const q = v => Math.max(-32768, Math.min(32767, Math.round(v * 32767)));
    for (let i = 0; i < n; i++) { b.writeInt16LE(q(L[i]), 44 + i * 4); b.writeInt16LE(q(R[i]), 46 + i * 4); }
    fs.writeFileSync(file, b);
}

// Повышение частоты в 2 раза (оконный sinc, по кругу)
function upsample2(x) {
    const n = x.length, y = new Float32Array(n * 2), T = 24;
    const k = [];
    for (let j = -T; j < T; j++) {
        const t = j + 0.5, w = 0.42 + 0.5 * Math.cos(Math.PI * t / T) + 0.08 * Math.cos(2 * Math.PI * t / T);
        k.push(Math.sin(Math.PI * t) / (Math.PI * t) * w);
    }
    for (let i = 0; i < n; i++) {
        y[2 * i] = x[i];
        let s = 0;
        for (let j = -T; j < T; j++) s += x[((i + j + 1) % n + n) % n] * k[j + T];
        y[2 * i + 1] = s;
    }
    return y;
}

// ---------- синтез ----------
const base = readWav(path.join(__dirname, 'base.wav'));
if (base.sr !== SR / 2) throw new Error('ожидался base.wav 22050 Гц');
const baseUp = upsample2(base.x);
const N = baseUp.length; // 1 008 000 сэмплов = 8 тактов
if (Math.abs(N / SR - BAR * 8) > 1e-3) throw new Error('длина base.wav не равна 8 тактам');

const L = new Float32Array(N), R = new Float32Array(N);     // слои (сухие)
const sendL = new Float32Array(N), sendR = new Float32Array(N); // посыл в реверб
const add = (buf, i, v) => { buf[((i % N) + N) % N] += v; };

let seed = 12345;
const rand = () => ((seed = (seed * 1103515245 + 12345) >>> 0) / 4294967296);

// Огибающая ADSR в секундах; t — время от начала ноты, len — длительность до отпускания
function env(t, len, a, d, s, r) {
    if (t < a) return t / a;
    if (t < len) return s + (1 - s) * Math.exp(-(t - a) / d);
    const atRel = s + (1 - s) * Math.exp(-(len - a) / d);
    return atRel * Math.exp(-(t - len) / r);
}

// Пэд: три слегка расстроенные пилы на голос (PolyBLEP), разведены по стерео; фильтр — ниже, общий
const padL = new Float32Array(N), padR = new Float32Array(N);
function blepSaw(ph, dt) {
    let v = 2 * ph - 1;
    if (ph < dt) { const t = ph / dt; v -= t + t - t * t - 1; }
    else if (ph > 1 - dt) { const t = (ph - 1) / dt; v -= t * t + t + t + 1; }
    return v;
}
BARS.forEach((bar, bi) => {
    const start = Math.round(bi * BAR * SR), len = BAR + 0.25, tail = 2.0;
    for (const m of bar.pad) {
        const det = [-9, 0, 9], pans = [-0.7, 0, 0.7];
        det.forEach((c, vi) => {
            const f = hz(m) * Math.pow(2, c / 1200), dt = f / SR;
            let ph = rand();
            const gl = Math.sqrt((1 - pans[vi]) / 2), gr = Math.sqrt((1 + pans[vi]) / 2);
            for (let i = 0; i < (len + tail) * SR; i++) {
                const e = env(i / SR, len, 0.9, 1.5, 0.75, 1.2);
                const v = blepSaw(ph, dt) * e * 0.12;
                add(padL, start + i, v * gl); add(padR, start + i, v * gr);
                ph += dt; if (ph >= 1) ph -= 1;
            }
        });
    }
});
// Тёплый фильтр: два однополюсных ФНЧ, частота среза медленно «дышит» раз в такт (кругом — два прохода)
function warmLP(x) {
    const y = new Float32Array(N);
    let a = 0, b = 0;
    for (let pass = 0; pass < 2; pass++)
        for (let i = 0; i < N; i++) {
            const fc = 700 + 350 * (0.5 - 0.5 * Math.cos(2 * Math.PI * (i / SR) / BAR));
            const k = 1 - Math.exp(-2 * Math.PI * fc / SR);
            a += k * (x[i] - a); b += k * (a - b);
            if (pass) y[i] = b;
        }
    return y;
}
const padLf = warmLP(padL), padRf = warmLP(padR);
for (let i = 0; i < N; i++) {
    L[i] += padLf[i] * MIX.pad; R[i] += padRf[i] * MIX.pad;
    sendL[i] += padLf[i] * MIX.pad; sendR[i] += padRf[i] * MIX.pad;
}

// Хор «у-у»: синус + немного обертонов, вибрато, медленная атака
BARS.forEach((bar, bi) => {
    const start = Math.round(bi * BAR * SR), len = BAR, tail = 2.5;
    bar.choir.forEach((m, ni) => {
        const f = hz(m), pan = ni ? 0.35 : -0.35;
        const gl = Math.sqrt((1 - pan) / 2), gr = Math.sqrt((1 + pan) / 2);
        let ph = 0;
        for (let i = 0; i < (len + tail) * SR; i++) {
            const t = i / SR;
            const vib = 1 + 0.004 * Math.sin(2 * Math.PI * 5.1 * t + ni) * Math.min(1, t / 1.5);
            ph += f * vib / SR;
            const w = 2 * Math.PI * ph;
            const v = (Math.sin(w) + 0.3 * Math.sin(2 * w) + 0.12 * Math.sin(3 * w)) * env(t, len, 1.4, 2, 0.8, 1.5) * MIX.choir;
            add(L, start + i, v * gl); add(R, start + i, v * gr);
            add(sendL, start + i, v * gl * 1.5); add(sendR, start + i, v * gr * 1.5);
        }
    });
});

// Суббас: чистый синус на корне аккорда, по центру, без реверба
BARS.forEach((bar, bi) => {
    const start = Math.round(bi * BAR * SR), len = BAR - 0.05, f = hz(bar.root);
    for (let i = 0; i < (len + 0.4) * SR; i++) {
        const t = i / SR;
        const v = (Math.sin(2 * Math.PI * f * t) + 0.15 * Math.sin(4 * Math.PI * f * t)) * env(t, len, 0.08, 2.5, 0.6, 0.12) * MIX.sub;
        add(L, start + i, v); add(R, start + i, v);
    }
});

// Колокольчики (FM): на 1-ю и 3-ю доли такта, поочерёдно слева/справа, в основном через реверб
BARS.forEach((bar, bi) => {
    bar.bell.forEach((m, ni) => {
        const start = Math.round((bi * BAR + ni * 2 * BEAT) * SR), f = hz(m);
        const pan = (bi * 2 + ni) % 2 ? 0.5 : -0.5;
        const gl = Math.sqrt((1 - pan) / 2), gr = Math.sqrt((1 + pan) / 2);
        for (let i = 0; i < 3.0 * SR; i++) {
            const t = i / SR;
            const idx = 2.2 * Math.exp(-t * 3);
            const a = Math.min(1, t / 0.004) * Math.exp(-t * 1.6);
            const v = Math.sin(2 * Math.PI * f * t + idx * Math.sin(2 * Math.PI * f * 3.5 * t)) * a * MIX.bell;
            add(L, start + i, v * gl * 0.6); add(R, start + i, v * gr * 0.6);
            add(sendL, start + i, v * gl); add(sendR, start + i, v * gr);
        }
    });
});

// Далёкий «удар» (литавра) на первую долю каждого такта
BARS.forEach((bar, bi) => {
    const start = Math.round(bi * BAR * SR);
    let ph = 0, lp = 0;
    for (let i = 0; i < 1.6 * SR; i++) {
        const t = i / SR;
        const f = hz(bar.root + 12) * (1 + 0.6 * Math.exp(-t * 18));
        ph += f / SR;
        lp += 0.05 * ((rand() * 2 - 1) - lp);
        const body = Math.sin(2 * Math.PI * ph) * Math.exp(-t * 2.8);
        const skin = lp * Math.exp(-t * 14) * 2;
        const v = (body + skin) * Math.min(1, t / 0.006) * MIX.boom;
        add(L, start + i, v); add(R, start + i, v);
        add(sendL, start + i, v * 0.8); add(sendR, start + i, v * 0.8);
    }
});

// Виолончель: пила через «корпус» (полосовой резонанс ~ 600 Гц + ФНЧ), смычковая атака, вибрато с задержкой
{
    const cl = new Float32Array(N);
    let beat = 0;
    for (const [m, dur] of CELLO) {
        const start = Math.round(beat * BEAT * SR), len = dur * BEAT - 0.04, f = hz(m);
        let ph = rand();
        for (let i = 0; i < (len + 0.6) * SR; i++) {
            const t = i / SR;
            const vib = 1 + 0.005 * Math.sin(2 * Math.PI * 5.4 * t) * Math.min(1, Math.max(0, t - 0.35) / 0.5);
            const dt = f * vib / SR;
            const bow = 1 + 0.08 * (rand() * 2 - 1) * Math.exp(-t * 8); // шорох смычка на атаке
            add(cl, start + i, blepSaw(ph, dt) * bow * env(t, len, 0.18, 1.2, 0.85, 0.35));
            ph += dt; if (ph >= 1) ph -= 1;
        }
        beat += dur;
    }
    // корпус: резонатор 2-го порядка (по кругу — два прохода) + ФНЧ
    const bq = (fc, q) => {
        const w = 2 * Math.PI * fc / SR, al = Math.sin(w) / (2 * q), a0 = 1 + al;
        return { b0: al / a0, b2: -al / a0, a1: -2 * Math.cos(w) / a0, a2: (1 - al) / a0 };
    };
    const body = bq(600, 0.9), y = new Float32Array(N);
    let x1 = 0, x2 = 0, y1 = 0, y2 = 0, lp = 0;
    for (let pass = 0; pass < 2; pass++)
        for (let i = 0; i < N; i++) {
            const x = cl[i];
            const o = body.b0 * x + body.b2 * x2 - body.a1 * y1 - body.a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = o;
            lp += 0.25 * (x - lp);
            if (pass) y[i] = o * 1.6 + lp * 0.35;
        }
    for (let i = 0; i < N; i++) {
        const v = y[i] * MIX.cello;
        L[i] += v * 0.62; R[i] += v * 0.78; // чуть правее центра
        sendL[i] += v * 0.6; sendR[i] += v * 0.75;
    }
}

// Малый барабан (маршевый, тихий): шум с подъёмом верхов + короткий тон корпуса, чуть левее
BARS.forEach((bar, bi) => {
    const pattern = bi === BARS.length - 1 ? SNARE_FILL : SNARE;
    pattern.forEach((vel, s) => {
        if (!vel) return;
        const start = Math.round((bi * BAR + s * BEAT / 4) * SR);
        let hp = 0, prev = 0, ph = 0;
        for (let i = 0; i < 0.35 * SR; i++) {
            const t = i / SR;
            const nz = rand() * 2 - 1;
            hp = 0.6 * (hp + nz - prev); prev = nz; // ФВЧ: «пружины»
            ph += 185 * (1 + 0.3 * Math.exp(-t * 60)) / SR;
            const v = (hp * Math.exp(-t * 16) * 0.8 + Math.sin(2 * Math.PI * ph) * Math.exp(-t * 35) * 0.6)
                * Math.min(1, t / 0.001) * vel * MIX.snare;
            add(L, start + i, v * 0.78); add(R, start + i, v * 0.62);
            add(sendL, start + i, v * 0.7); add(sendR, start + i, v * 0.55);
        }
    });
});

// Исходная мелодия — по центру, немного в реверб
for (let i = 0; i < N; i++) {
    const v = baseUp[i] * MIX.base;
    L[i] += v; R[i] += v;
    sendL[i] += v * MIX.reverbBase / MIX.reverbLayers; sendR[i] += v * MIX.reverbBase / MIX.reverbLayers;
}

// ---------- реверб (Freeverb), три круга, берём последний — устоявшийся хвост ----------
function freeverb(inL, inR) {
    const room = 0.86, damp = 0.35, spread = 23;
    const combs = [1557, 1617, 1491, 1422, 1277, 1356, 1188, 1116], aps = [556, 441, 341, 225];
    const mk = d => ({ buf: new Float32Array(d), i: 0, f: 0 });
    const ch = off => ({ c: combs.map(d => mk(d + off)), a: aps.map(d => mk(d + off)) });
    const cl = ch(0), cr = ch(spread);
    const outL = new Float32Array(N), outR = new Float32Array(N);
    function proc(c, x) {
        let s = 0;
        for (const cb of c.c) {
            const y = cb.buf[cb.i];
            cb.f = y * (1 - damp) + cb.f * damp;
            cb.buf[cb.i] = x + cb.f * room;
            cb.i = (cb.i + 1) % cb.buf.length;
            s += y;
        }
        for (const ap of c.a) {
            const b = ap.buf[ap.i];
            ap.buf[ap.i] = s + b * 0.5;
            ap.i = (ap.i + 1) % ap.buf.length;
            s = b - s;
        }
        return s;
    }
    for (let pass = 0; pass < 3; pass++)
        for (let i = 0; i < N; i++) {
            const x = (inL[i] + inR[i]) * 0.015;
            const yl = proc(cl, x), yr = proc(cr, x);
            if (pass === 2) { outL[i] = yl * 0.8 + yr * 0.2; outR[i] = yr * 0.8 + yl * 0.2; }
        }
    return [outL, outR];
}
const [wL, wR] = freeverb(sendL, sendR);
for (let i = 0; i < N; i++) { L[i] += wL[i] * MIX.reverbLayers; R[i] += wR[i] * MIX.reverbLayers; }

// ---------- мастер: убрать постоянку, мягко прижать пики, пик −1 dBFS ----------
// Unity нормализует клип по пику, поэтому пик-фактор держим как у исходника (~3,2), иначе в игре станет тише
let rawPeak = 0;
for (const ch of [L, R]) {
    let m = 0; for (const v of ch) m += v; m /= N;
    for (let i = 0; i < N; i++) { ch[i] -= m; rawPeak = Math.max(rawPeak, Math.abs(ch[i])); }
}
for (const ch of [L, R])
    for (let i = 0; i < N; i++) ch[i] = Math.tanh(ch[i] * 1.4 / rawPeak);
let peak = 0, rms = 0;
for (let i = 0; i < N; i++) { peak = Math.max(peak, Math.abs(L[i]), Math.abs(R[i])); rms += L[i] * L[i] + R[i] * R[i]; }
rms = Math.sqrt(rms / N / 2);
const g = 0.891 / peak;
for (let i = 0; i < N; i++) { L[i] *= g; R[i] *= g; }
writeWav(OUT, L, R);

let bp = 0, br = 0; for (const v of base.x) { bp = Math.max(bp, Math.abs(v)); br += v * v; } br = Math.sqrt(br / base.x.length);
console.log(`${OUT}\n${(N / SR).toFixed(3)} с, пик-фактор было ${(bp / br).toFixed(2)}, стало ${(peak / rms).toFixed(2)}`);
console.log(`стык петли: L ${(L[N - 1] - L[0]).toFixed(4)}, R ${(R[N - 1] - R[0]).toFixed(4)}`);
