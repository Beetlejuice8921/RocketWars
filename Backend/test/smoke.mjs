// Проверка сервера рейтинга на локальном wrangler dev (с .dev.vars из .dev.vars.example):
//   node test/smoke.mjs [http://127.0.0.1:8787]
import { createHmac, randomBytes } from 'node:crypto';
import assert from 'node:assert/strict';

const API = process.argv[2] || 'http://127.0.0.1:8787';
const BOT_TOKEN = '123456:TEST-TOKEN-FOR-LOCAL-CHECKS';

async function call(method, path, body, token) {
  const res = await fetch(API + path, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  return { status: res.status, data: await res.json() };
}

/** initData, подписанный так же, как это делает Telegram. */
function initData(user, botToken = BOT_TOKEN) {
  const fields = { auth_date: String(Math.floor(Date.now() / 1000)), query_id: 'AAH' + user.id, user: JSON.stringify(user) };
  const check = Object.keys(fields).sort().map(k => `${k}=${fields[k]}`).join('\n');
  const secret = createHmac('sha256', 'WebAppData').update(botToken).digest();
  const hash = createHmac('sha256', secret).update(check).digest('hex');
  return new URLSearchParams({ ...fields, hash }).toString();
}

async function tg(id, name) {
  const r = await call('POST', '/v1/auth', { provider: 'telegram', data: initData({ id, first_name: name }) });
  assert.equal(r.status, 200, JSON.stringify(r.data));
  assert.equal(r.data.verified, true);
  return r.data.token;
}

const row = (id, team, extra = {}) => ({ id, team, name: 'P' + id, downed: 0, deaths: 0, revives: 0, silos: 0, falls: 0, tk: 0, ach: 0, left: false, bot: false, ...extra });

let ok = 0;
const check = (name, cond, info) => { assert.ok(cond, name + ' ' + JSON.stringify(info ?? '')); ok++; console.log('ok', name); };

// подпись чужим ключом — отказ
const bad = await call('POST', '/v1/auth', { provider: 'telegram', data: initData({ id: 1, first_name: 'X' }, '999:WRONG') });
check('telegram: wrong signature rejected', bad.status === 401, bad);

const base = 7000000 + Math.floor(Math.random() * 1e6);
const [a, b, c] = [await tg(base + 1, 'Alice'), await tg(base + 2, 'Bob'), await tg(base + 3, 'Carl')];

// матч 3 человека + бот: Alice и Carl — команда 0 (победа 4:2), Bob — команда 1
const start = await call('POST', '/v1/match/start', { platform: 'telegram' }, a);
check('match start', start.status === 200 && /^[0-9a-f]{20}$/.test(start.data.matchId), start);
const matchId = start.data.matchId;
const rows = [row(10, 0, { downed: 5, ach: 0b101 }), row(11, 1, { downed: 2, revives: 1 }), row(12, 0, { tk: 1 }), row(13, 1, { bot: true, downed: 1 })];
const report = (me, r = rows) => ({ matchId, me, score0: 4, score1: 2, winner: 0, rows: r });

const r1 = await call('POST', '/v1/match/report', report(10), a);
check('first report waits for quorum', r1.status === 200 && r1.data.status === 'pending', r1);

const r2 = await call('POST', '/v1/match/report', report(11), b);
check('second matching report → credited', r2.data.status === 'credited' && r2.data.points === 10 + 4 + 2, r2);

const r1b = await call('GET', `/v1/match/result?matchId=${matchId}`, null, a);
// 10 участие + 20 победа + 10 за 5 врагов + 10 за 2 награды
check('host credited after quorum', r1b.data.status === 'credited' && r1b.data.points === 50, r1b);

const forged = rows.map(x => x.id === 12 ? { ...x, downed: 30 } : x);
const r3 = await call('POST', '/v1/match/report', report(12, forged), c);
check('different table → rejected', r3.data.status === 'rejected', r3);

const r3b = await call('POST', '/v1/match/report', report(12), c);
check('second report from same player → 409', r3b.status === 409, r3b);

const steal = await call('POST', '/v1/match/report', report(10), c);
check('claiming someone else\'s row → 409', steal.status === 409, steal);

const repeat = await call('POST', '/v1/match/report', report(10), a);
check('resend of the same report is idempotent', repeat.status === 200 && repeat.data.points === 50, repeat);

const badScore = await call('POST', '/v1/match/report', { ...report(10), score0: 3 }, a);
check('impossible score → 400', badScore.status === 400, badScore);

const botRow = await call('POST', '/v1/match/report', report(13), c);
check('bot row cannot be claimed', botRow.status === 400, botRow);

// тренировка: гость один с ботом — 3 очка, не больше 30 за неделю
const key = randomBytes(16).toString('hex');
const g = (await call('POST', '/v1/auth', { provider: 'guest', data: key })).data;
check('guest auth', g.token && g.verified === false, g);
let total = 0;
for (let i = 0; i < 11; i++) {
  const m = (await call('POST', '/v1/match/start', {}, g.token));
  if (m.status === 429) { console.log('   (rate limit hit at match', i + 1, ', as designed)'); break; }
  const r = await call('POST', '/v1/match/report', {
    matchId: m.data.matchId, me: 1, score0: 0, score1: 4, winner: 1, rows: [row(1, 1, { downed: 9 }), row(2, 0, { bot: true })],
  }, g.token);
  total += r.data.points;
}
check('practice gives 3 points per match', total === 9, total); // 3 матча в минуту — дальше 429

const lb = await call('GET', '/v1/leaderboard?season=week', null, a);
const alice = lb.data.top.find(x => x.me);
check('leaderboard shows me', alice && alice.points === 50 && lb.data.me.rank === alice.rank, lb.data.me);
check('leaderboard resetsIn', lb.data.resetsIn > 0 && lb.data.resetsIn <= 7 * 86400, lb.data.resetsIn);
const all = await call('GET', '/v1/leaderboard?season=all&limit=5');
check('anonymous all-time leaderboard', all.status === 200 && all.data.me === undefined && all.data.top.length > 0, all.data);

const noToken = await call('POST', '/v1/match/start', {});
check('no token → 401', noToken.status === 401, noToken);

console.log(`\n${ok} checks passed`);
