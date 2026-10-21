// Фон в матче (ветер): base.wav (петля 6,5 с, сведена кроссфейдом из Extras/NewSounds/Wind_Ambience.wav) → громче и разборчивее.
// Unity нормализует клип по пику, поэтому громкость в игре задаёт пик-фактор: прижимаем порывы и поднимаем верха,
// чтобы шум ветра пробивался через динамики ноутбука. Фильтры считаются по кругу (два прохода) — стык петли не страдает.
// Запуск: node build.js [out.wav]   (по умолчанию пишет в Assets/_Project/Resources/Sounds/AmbientMatch.wav)
const fs = require('fs');
const path = require('path');

const OUT = process.argv[2] || path.join(__dirname, '../../../Assets/_Project/Resources/Sounds/AmbientMatch.wav');
const PRESENCE_DB = 6;   // подъём выше ~1,5 кГц
const PRESENCE_HZ = 1500;
const DRIVE = 1.6;       // насколько сильно прижимать порывы (1 — почти без изменений)

function readWav(file) {
    const b = fs.readFileSync(file);
    let p = 12, fmt, data;
    while (p + 8 <= b.length) {
        const id = b.toString('ascii', p, p + 4), size = b.readUInt32LE(p + 4);
        if (id === 'fmt ') fmt = { ch: b.readUInt16LE(p + 10), sr: b.readUInt32LE(p + 12) };
        if (id === 'data') data = b.subarray(p + 8, p + 8 + size);
        p += 8 + size + (size & 1);
    }
    const n = data.length / 2 / fmt.ch, chans = [];
    for (let c = 0; c < fmt.ch; c++) {
        const x = new Float32Array(n);
        for (let i = 0; i < n; i++) x[i] = data.readInt16LE((i * fmt.ch + c) * 2) / 32768;
        chans.push(x);
    }
    return { sr: fmt.sr, chans };
}

function writeWav(file, sr, chans) {
    const ch = chans.length, n = chans[0].length, b = Buffer.alloc(44 + n * 2 * ch);
    b.write('RIFF', 0); b.writeUInt32LE(36 + n * 2 * ch, 4); b.write('WAVEfmt ', 8);
    b.writeUInt32LE(16, 16); b.writeUInt16LE(1, 20); b.writeUInt16LE(ch, 22);
    b.writeUInt32LE(sr, 24); b.writeUInt32LE(sr * 2 * ch, 28); b.writeUInt16LE(2 * ch, 32); b.writeUInt16LE(16, 34);
    b.write('data', 36); b.writeUInt32LE(n * 2 * ch, 40);
    for (let i = 0; i < n; i++)
        for (let c = 0; c < ch; c++)
            b.writeInt16LE(Math.max(-32768, Math.min(32767, Math.round(chans[c][i] * 32767))), 44 + (i * ch + c) * 2);
    fs.writeFileSync(file, b);
}

const stats = chans => {
    let pk = 0, e = 0, n = 0;
    for (const x of chans) for (const v of x) { pk = Math.max(pk, Math.abs(v)); e += v * v; n++; }
    return { pk, rms: Math.sqrt(e / n) };
};

const { sr, chans } = readWav(path.join(__dirname, 'base.wav'));
const before = stats(chans);

// Полка верхов: x + g·(x − ФНЧ(x)), ФНЧ однополюсный, по кругу
const g = Math.pow(10, PRESENCE_DB / 20) - 1, k = 1 - Math.exp(-2 * Math.PI * PRESENCE_HZ / sr);
const out = chans.map(x => {
    const n = x.length, y = new Float32Array(n);
    let lp = 0;
    for (let pass = 0; pass < 2; pass++)
        for (let i = 0; i < n; i++) {
            lp += k * (x[i] - lp);
            if (pass) y[i] = x[i] + g * (x[i] - lp);
        }
    return y;
});

// Мягкое ограничение без памяти (стык не ломает): громкие порывы уходят в tanh, тихий шум почти не меняется
const ref = stats(out).rms * 4;
for (const y of out) for (let i = 0; i < y.length; i++) y[i] = Math.tanh(y[i] / ref * DRIVE);
const after = stats(out);
for (const y of out) for (let i = 0; i < y.length; i++) y[i] *= 0.891 / after.pk; // пик −1 dBFS

writeWav(OUT, sr, out);
const db = v => (20 * Math.log10(v)).toFixed(1);
console.log(`${OUT}\nпик-фактор ${db(before.pk / before.rms)} дБ → ${db(after.pk / after.rms)} дБ: в игре громче на ~${db((after.rms / after.pk) / (before.rms / before.pk))} дБ`);
console.log('стык:', out.map(y => (y[y.length - 1] - y[0]).toFixed(4)).join(' '));
