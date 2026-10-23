// Проверка бота и стримов на локальном wrangler dev (.dev.vars из .dev.vars.example, запуск с --test-scheduled):
//   node test/bot.mjs [http://127.0.0.1:8787]
// Поднимает подменный Telegram API на 127.0.0.1:8790 (TELEGRAM_API в .dev.vars) и записывает, что бот отправил.
import { createHmac } from 'node:crypto';
import { createServer } from 'node:http';
import assert from 'node:assert/strict';

const API = process.argv[2] || 'http://127.0.0.1:8787';
const BOT_TOKEN = '123456:TEST-TOKEN-FOR-LOCAL-CHECKS';
const SECRET = 'local-webhook-secret';
const ADMIN = 100;

// ---------- подменный Telegram ----------

const sent = [];
const blocked = new Set(); // эти чаты «заблокировали бота» — 403
const mock = createServer((req, res) => {
  let raw = '';
  req.on('data', c => raw += c);
  req.on('end', () => {
    const method = req.url.split('/').pop();
    const payload = raw ? JSON.parse(raw) : {};
    sent.push({ method, ...payload });
    res.setHeader('Content-Type', 'application/json');
    if (method === 'sendMessage' && blocked.has(String(payload.chat_id))) {
      res.statusCode = 403;
      return res.end(JSON.stringify({ ok: false, error_code: 403, description: 'Forbidden: bot was blocked by the user' }));
    }
    res.end(JSON.stringify({ ok: true, result: true }));
  });
});
await new Promise(r => mock.listen(8790, '127.0.0.1', r));

// ---------- помощники ----------

async function call(method, path, body, token, headers = {}) {
  const res = await fetch(API + path, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}), ...headers },
    body: body ? JSON.stringify(body) : undefined,
  });
  let data = null;
  try { data = await res.json(); } catch { }
  return { status: res.status, data };
}

function initData(user) {
  const fields = { auth_date: String(Math.floor(Date.now() / 1000)), query_id: 'AAH' + user.id, user: JSON.stringify(user) };
  const check = Object.keys(fields).sort().map(k => `${k}=${fields[k]}`).join('\n');
  const secret = createHmac('sha256', 'WebAppData').update(BOT_TOKEN).digest();
  const hash = createHmac('sha256', secret).update(check).digest('hex');
  return new URLSearchParams({ ...fields, hash }).toString();
}

async function login(user) {
  const r = await call('POST', '/v1/auth', { provider: 'telegram', data: initData(user) });
  assert.equal(r.status, 200, JSON.stringify(r.data));
  return r.data;
}

let updateId = 1;
const hook = (from, text) => call('POST', '/tg/webhook',
  { update_id: updateId++, message: { message_id: updateId, from, chat: { id: from.id, type: 'private' }, date: 0, text } },
  null, { 'X-Telegram-Bot-Api-Secret-Token': SECRET });

const cron = () => fetch(API + '/__scheduled?cron=*+*+*+*+*');
const sleep = ms => new Promise(r => setTimeout(r, ms));
const to = id => sent.filter(m => m.method === 'sendMessage' && String(m.chat_id) === String(id));
const last = id => to(id).at(-1);
const buttons = m => (m && m.reply_markup ? m.reply_markup.inline_keyboard.flat() : []);

let ok = 0;
const check = (name, cond, info) => { assert.ok(cond, name + ' ' + JSON.stringify(info ?? '')); ok++; console.log('ok', name); };

// ---------- сценарий ----------

const base = 8000000 + Math.floor(Math.random() * 1e6);
const viewerA = { id: base + 1, first_name: 'Anna', language_code: 'ru' };
const viewerB = { id: base + 2, first_name: 'Bob', language_code: 'en', allows_write_to_pm: true };
const streamerS = { id: base + 3, first_name: 'Streamy', language_code: 'ru' };
const outsider = { id: base + 4, first_name: 'Olga', language_code: 'ru' };
const admin = { id: ADMIN, first_name: 'Admin', language_code: 'ru' };
const room = 'K7Q2ZX';

const bad = await call('POST', '/tg/webhook', { update_id: 1 }, null, { 'X-Telegram-Bot-Api-Secret-Token': 'wrong' });
check('webhook: wrong secret rejected', bad.status === 401, bad);

await hook(viewerA, '/start');
check('/start: welcome with Play button', /Rocket Wars/.test(last(viewerA.id)?.text) && buttons(last(viewerA.id))[0]?.url.endsWith('/play'), last(viewerA.id));

await hook(viewerA, '/start room_ab12cd');
check('/start room_ payload: join button opens that room', buttons(last(viewerA.id))[0]?.url.endsWith('?startapp=room_AB12CD'), last(viewerA.id));

await hook(viewerA, '/room k7q2zx');
check('/room CODE: join button', buttons(last(viewerA.id))[0]?.url.endsWith(`?startapp=room_${room}`), last(viewerA.id));
await hook(viewerA, '/room 12');
check('/room bad code: usage hint', /ABC123/.test(last(viewerA.id)?.text), last(viewerA.id));

await hook(viewerA, '/stream');
check('/stream: nobody streaming', /никто не стримит/.test(last(viewerA.id)?.text), last(viewerA.id));

await hook(streamerS, '/id');
check('/id: shows Telegram ID', last(streamerS.id)?.text.includes(String(streamerS.id)), last(streamerS.id));

const beforeAdd = to(viewerA.id).length;
await hook(viewerA, `/streamer_add ${streamerS.id} https://twitch.tv/streamy Streamy`);
check('non-admin: admin commands are ignored', to(viewerA.id).length === beforeAdd, last(viewerA.id));
await hook(admin, `/streamer_add ${streamerS.id} https://twitch.tv/streamy Streamy TV`);
check('admin: /streamer_add', /Стример добавлен: Streamy TV/.test(last(ADMIN)?.text), last(ADMIN));

// игроки входят в игру: B разрешил боту писать из Mini App — подписан
const b = await login(viewerB);
const s = await login(streamerS);
const o = await login(outsider);
const a = await login(viewerA);
check('auth: skins list present (empty)', Array.isArray(b.skins) && b.skins.length === 0, b);

const notPartner = await call('POST', '/v1/stream/open', { roomCode: room }, o.token);
check('stream/open: not a partner', notPartner.status === 200 && notPartner.data.partner === false, notPartner);
const noAnnounce = await call('POST', '/v1/stream/announce', { kind: 'now' }, o.token);
check('stream/announce: not a partner → 403', noAnnounce.status === 403, noAnnounce);

const open = await call('POST', '/v1/stream/open', { roomCode: room.toLowerCase() }, s.token);
check('stream/open: partner', open.data.partner === true && open.data.announcesLeft === 3 && open.data.name === 'Streamy TV', open);

await hook(viewerA, '/stream');
check('/stream: live stream with join + watch buttons',
  buttons(last(viewerA.id)).some(x => x.url.endsWith(`room_${room}`)) && buttons(last(viewerA.id)).some(x => x.url === 'https://twitch.tv/streamy'), last(viewerA.id));

const before = sent.length;
const ann = await call('POST', '/v1/stream/announce', { kind: 'now' }, s.token);
check('announce: queued for A and B (not the streamer)', ann.status === 200 && ann.data.queued >= 2 && ann.data.announcesLeft === 2, ann);
await sleep(1500);
const annA = to(viewerA.id).find((m, i) => sent.indexOf(m) >= before);
const annB = to(viewerB.id).find((m, i) => sent.indexOf(m) >= before);
check('announce: A got Russian text with room + watch buttons',
  annA && /Streamy TV/.test(annA.text) && /прямо сейчас/.test(annA.text) && buttons(annA).length === 2 && buttons(annA)[0].url.endsWith(`room_${room}`), annA);
check('announce: B got English text', annB && /right now/.test(annB.text), annB);
check('announce: streamer did not get own announcement', !to(streamerS.id).some(m => sent.indexOf(m) >= before), null);

const again = await call('POST', '/v1/stream/announce', { kind: 'soon' }, s.token);
check('announce again within 10 min → 429', again.status === 429 && again.data.error === 'announce_too_soon', again);

// матчи на стриме
const p1 = await call('POST', '/v1/stream/played', { roomCode: room, place: 3, won: false }, b.token);
check('played: viewer B recorded', p1.data.status === 'ok', p1);
const p1dup = await call('POST', '/v1/stream/played', { roomCode: room, place: 1, won: true }, b.token);
check('played: second report within 30 s ignored (status ok, no change)', p1dup.data.status === 'ok', p1dup);
const p2 = await call('POST', '/v1/stream/played', { roomCode: room, place: 1, won: true }, a.token);
check('played: viewer A recorded', p2.data.status === 'ok', p2);
const pS = await call('POST', '/v1/stream/played', { roomCode: room, place: 1, won: true }, s.token);
check('played: streamer is not a viewer', pS.data.status === 'no_stream', pS);
const pBad = await call('POST', '/v1/stream/played', { roomCode: 'ZZZZZZ', place: 1, won: true }, a.token);
check('played: unknown room', pBad.data.status === 'no_stream', pBad);

// стример закрыл комнату → итоги и скин
const beforeResults = sent.length;
const close = await call('POST', '/v1/stream/close', {}, s.token);
check('stream/close', close.status === 200, close);
await sleep(1500);
await cron();
await sleep(500);
const resA = to(viewerA.id).find(m => sent.indexOf(m) >= beforeResults);
const resB = to(viewerB.id).find(m => sent.indexOf(m) >= beforeResults);
check('results: A — 1 match, 1 win, best place 1, skin unlocked',
  resA && /Матчей: 1, побед: 1, лучшее место: 1/.test(resA.text) && /Зритель стрима/.test(resA.text), resA);
check('results: B (English) — 1 match, 0 wins, place 3', resB && /Matches: 1, wins: 0, best place: 3/.test(resB.text), resB);
const bAgain = await login(viewerB);
check('auth: stream skin granted', bAgain.skins.includes(3), bAgain);

await hook(viewerA, '/stream');
check('/stream after close: nobody streaming', /никто не стримит/.test(last(viewerA.id)?.text), last(viewerA.id));

// второй стрим: скин уже есть — в итогах о нём не пишем
await call('POST', '/v1/stream/open', { roomCode: 'QQQQQ1' }, s.token);
await call('POST', '/v1/stream/played', { roomCode: 'QQQQQ1', place: 2, won: false }, b.token);
const before2 = sent.length;
await call('POST', '/v1/stream/close', {}, s.token);
await sleep(1500);
await cron();
await sleep(500);
const resB2 = to(viewerB.id).find(m => sent.indexOf(m) >= before2);
check('results again: no skin line when already owned', resB2 && !/skin/.test(resB2.text), resB2);

// отписка и блокировка
await hook(viewerA, '/stop');
check('/stop: confirmation', /Больше не пришлю/.test(last(viewerA.id)?.text), last(viewerA.id));
blocked.add(String(viewerB.id));
await hook(admin, '/broadcast Новая карта!');
check('admin: /broadcast queued', /Поставлено в очередь: \d+/.test(last(ADMIN)?.text), last(ADMIN));
const beforeB = sent.length;
await cron();
await sleep(500);
check('broadcast: A (stopped) got nothing', !to(viewerA.id).some(m => sent.indexOf(m) >= beforeB), null);
await hook(admin, '/broadcast Ещё раз');
const beforeB2 = sent.length;
await cron();
await sleep(500);
check('broadcast: B blocked the bot → unsubscribed after 403, not retried',
  !to(viewerB.id).some(m => sent.indexOf(m) >= beforeB2), null);
await hook(admin, '/stats');
check('admin: /stats', /Подписчиков: \d+ активных/.test(last(ADMIN)?.text), last(ADMIN));

// рейтинг: короткий матч стрим-комнаты (до 2 побед) принимается
const start = await call('POST', '/v1/match/start', { platform: 'telegram' }, a.token);
const row = (id, team, extra = {}) => ({ id, team, name: 'P' + id, downed: 0, deaths: 0, revives: 0, silos: 0, falls: 0, tk: 0, ach: 0, left: false, bot: false, ...extra });
const rows = [row(1, 0, { downed: 2 }), row(2, 1)];
const rep = me => ({ matchId: start.data.matchId, me, wins: 2, score0: 2, score1: 1, winner: 0, rows });
await call('POST', '/v1/match/report', rep(1), a.token);
const r2 = await call('POST', '/v1/match/report', rep(2), b.token);
check('rating: match to 2 wins accepted, points halved (10 base → 5)', r2.data.status === 'credited' && r2.data.points === 5, r2);
const badWins = await call('POST', '/v1/match/report', { ...rep(1), wins: 2, score0: 4 }, a.token);
check('rating: score above wins rejected', badWins.status === 400, badWins);

// /stats после матча: заходы, матч и матч с живыми соперниками попали в «сегодня»
await call('POST', '/v1/match/start', { platform: 'editor' }, b.token);
await hook(admin, '/stats');
const stats = last(ADMIN)?.text || '';
const today = label => Number((stats.match(new RegExp(label + ': (\\d+) / ')) || [])[1] || 0);
check('admin: /stats counts players and matches today',
  today('Заходили в игру') >= 2 && today('Сыграли матч') >= 2 && today('Матчей') >= 1 && today('Из них с живыми соперниками') >= 1 &&
  /telegram \d+/.test(stats) && /тесты \(редактор, dev-сборки\) 1/.test(stats) && !/editor/.test(stats), stats);

console.log(`\n${ok} checks passed`);
mock.close();
