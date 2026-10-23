// Rocket Wars — бот @RocketWarsGameBot и стримы с подписчиками.
// • Вебхук Telegram (/tg/webhook): /start, /room КОД, /stream, /sub, /stop, /id и кнопка карточки подписки; админу — список стримеров-партнёров и рассылка.
// • Игра стримера: открыть стрим (/v1/stream/open, раз в несколько минут — «я ещё тут»), анонс всем подписчикам
//   (/v1/stream/announce), закрыть (/v1/stream/close). Игроки после каждого матча сообщают своё место (/v1/stream/played).
// • Сообщения не шлются сразу, а ставятся в outbox: cron раз в минуту отправляет пачку (лимиты Telegram и Cloudflare).
// • Когда стрим закрыт (или игра стримера пропала), каждому сыгравшему — итоги и скин-награда «Зритель стрима».
// Подробности — Backend/README.md.

import { HttpError, json, now, body, requireToken, cleanName, randomId, timingSafeEqual, statDay, statDayStart } from './common.js';

const APP_URL = 'https://t.me/RocketWarsGameBot/play';
export const STREAM_SKIN = 3;                // Core/Skins: «Зритель стрима»
const STREAM_IDLE_SEC = 20 * 60;             // игра стримера не отмечалась столько — стрим закончен
const STREAM_MAX_SEC = 12 * 3600;
const MAX_ANNOUNCES = 3;                     // анонсов за один стрим
const ANNOUNCE_GAP_SEC = 10 * 60;
const PLAYED_GAP_SEC = 30;                   // отчёт о матче от игрока — не чаще (матч короче не бывает)
const MAX_PLACE = 40;
const DEFAULT_BATCH = 40;                    // сообщений за запуск: бесплатный Worker — до 50 запросов наружу
const MAX_ATTEMPTS = 3;
const ROOM_CODE = /^[A-Z0-9]{6}$/;

// ---------- тексты (ru — для русскоязычных и соседей, остальным en) ----------

const T = {
  ru: {
    welcome: name => `Привет, ${name}! 🚀 Rocket Wars — ракетные дуэли островов прямо в Telegram.\n\n/stream — кто сейчас стримит\n/room КОД — войти в комнату по коду\n/sub — подписаться на анонсы\n/stop — отписаться`,
    cardOn: '🔔 <b>Анонсы стримов: включены</b>\nПришлю сообщение, когда стример начнёт играть с подписчиками, и итоги после стрима.',
    cardOff: '🔕 <b>Анонсы стримов: выключены</b>\nБольше не пришлю анонсы. Включить — кнопкой ниже или /sub',
    kbSub: '🔔 Подписка',
    kbStreams: '📺 Кто стримит',
    subscribe: '🔔 Подписаться на анонсы',
    unsubscribe: '🔕 Отписаться',
    toastOn: '✅ Подписка включена',
    toastOff: 'Подписка выключена',
    play: '🚀 Играть',
    joinRoom: code => `🚀 Войти в комнату ${code}`,
    joinStream: name => `🚀 Играть у ${name}`,
    watch: '📺 Смотреть стрим',
    room: code => `Комната <b>${code}</b> — жми кнопку, игра откроется сразу в ней.`,
    roomUsage: 'Напиши код комнаты из 6 знаков: /room ABC123',
    noStreams: 'Сейчас никто не стримит. Я пришлю анонс, когда начнётся.',
    streams: 'Сейчас играют с подписчиками:',
    announceNow: (name, watch) => `🔴 <b>${esc(name)}</b> играет в Rocket Wars с подписчиками прямо сейчас!${watch ? '\nСмотри стрим и залетай в комнату — места занимают по очереди.' : '\nЗалетай в комнату — места занимают по очереди.'}`,
    announceSoon: name => `⏰ Через 15 минут <b>${esc(name)}</b> играет в Rocket Wars с подписчиками! Комната уже открыта — можно занять место в очереди.`,
    results: (name, s, newSkin) => `🏁 Стрим у <b>${esc(name)}</b> закончился — спасибо, что играл!\nМатчей: ${s.matches}, побед: ${s.wins}, лучшее место: ${s.best_place}.` +
      (newSkin ? '\n\n🎖 Тебе открыт скин «Зритель стрима» — выбери его в игре (кнопка «Скин» рядом с именем).' : ''),
    id: id => `Твой Telegram ID: <code>${id}</code>`,
  },
  en: {
    welcome: name => `Hi, ${name}! 🚀 Rocket Wars — rocket duels between islands right in Telegram.\n\n/stream — who is streaming now\n/room CODE — join a room by code\n/sub — subscribe to announcements\n/stop — unsubscribe`,
    cardOn: '🔔 <b>Stream announcements: on</b>\nI will message you when a streamer starts playing with viewers, and send the results after the stream.',
    cardOff: '🔕 <b>Stream announcements: off</b>\nNo more announcements. Turn them on with the button below or /sub',
    kbSub: '🔔 Subscription',
    kbStreams: '📺 Who is streaming',
    subscribe: '🔔 Subscribe to announcements',
    unsubscribe: '🔕 Unsubscribe',
    toastOn: '✅ Subscribed',
    toastOff: 'Unsubscribed',
    play: '🚀 Play',
    joinRoom: code => `🚀 Join room ${code}`,
    joinStream: name => `🚀 Play with ${name}`,
    watch: '📺 Watch the stream',
    room: code => `Room <b>${code}</b> — tap the button, the game opens right in it.`,
    roomUsage: 'Send a 6-character room code: /room ABC123',
    noStreams: 'Nobody is streaming right now. I will let you know when it starts.',
    streams: 'Playing with viewers right now:',
    announceNow: (name, watch) => `🔴 <b>${esc(name)}</b> is playing Rocket Wars with viewers right now!${watch ? '\nWatch the stream and jump into the room — players take turns.' : '\nJump into the room — players take turns.'}`,
    announceSoon: name => `⏰ In 15 minutes <b>${esc(name)}</b> plays Rocket Wars with viewers! The room is already open — grab a spot in the queue.`,
    results: (name, s, newSkin) => `🏁 The stream by <b>${esc(name)}</b> is over — thanks for playing!\nMatches: ${s.matches}, wins: ${s.wins}, best place: ${s.best_place}.` +
      (newSkin ? '\n\n🎖 You unlocked the "Stream Viewer" skin — pick it in the game (the "Skin" button next to your name).' : ''),
    id: id => `Your Telegram ID: <code>${id}</code>`,
  },
};

const RU_LANGS = ['ru', 'uk', 'be', 'kk', 'uz'];
const langOf = code => RU_LANGS.includes(String(code || '').slice(0, 2)) ? 'ru' : 'en';

function esc(s) {
  return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

const appUrl = env => env.APP_URL || APP_URL;
const roomUrl = (env, code) => `${appUrl(env)}?startapp=room_${code}`;
const keyboard = rows => JSON.stringify({ inline_keyboard: rows.filter(r => r.length) });
/** Постоянные кнопки под полем ввода (вместо набора команд); нажатие присылает их текст — см. KB_ACTIONS. */
const replyKeyboard = L => JSON.stringify({ keyboard: [[{ text: L.kbSub }, { text: L.kbStreams }]], resize_keyboard: true, is_persistent: true });
// текст кнопки на любом языке → команда (язык Telegram у игрока мог смениться после /start)
const KB_ACTIONS = new Map(Object.values(T).flatMap(L => [[L.kbSub, '/status'], [L.kbStreams, '/stream']]));

// ---------- Telegram ----------

async function tg(env, method, payload) {
  const base = env.TELEGRAM_API || 'https://api.telegram.org';
  const res = await fetch(`${base}/bot${env.BOT_TOKEN}/${method}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
  let data = null;
  try { data = await res.json(); } catch { }
  return { status: res.status, data: data || { ok: false } };
}

function reply(env, chatId, text, markup) {
  const payload = { chat_id: chatId, text, parse_mode: 'HTML', disable_web_page_preview: true };
  if (markup)
    payload.reply_markup = JSON.parse(markup);
  return tg(env, 'sendMessage', payload);
}

/** Карточка подписки в чате: состояние и кнопка «Подписаться» / «Отписаться» (нажатие правит эту же карточку). */
const subCard = (env, L, on) => ({
  text: on ? L.cardOn : L.cardOff,
  markup: keyboard([[{ text: on ? L.unsubscribe : L.subscribe, callback_data: on ? 'sub:0' : 'sub:1' }], [{ text: L.play, url: appUrl(env) }]]),
});

function sendCard(env, chat, L, on) {
  const card = subCard(env, L, on);
  return reply(env, chat, card.text, card.markup);
}

const adminIds = env => String(env.ADMIN_IDS || '').split(/[\s,]+/).filter(Boolean);

/**
 * Подписчик: нажал /start или кнопку «Подписаться» в игре. Язык обновляется при каждом заходе.
 * onlyNew — вход в Mini App с разрешением писать: новых подписываем, а отписавшегося (/stop) не возвращаем.
 */
export async function subscribe(env, user, source, onlyNew = false) {
  const t = now();
  await env.DB.prepare(
    `INSERT INTO subscribers (tg_id, name, lang, active, source, created_at, updated_at) VALUES (?1, ?2, ?3, 1, ?4, ?5, ?5)
     ON CONFLICT (tg_id) DO UPDATE SET name = excluded.name, lang = excluded.lang,
       active = CASE WHEN ?6 = 1 THEN active ELSE 1 END, updated_at = excluded.updated_at`)
    .bind(String(user.id), cleanName(user.first_name || user.username || ''), langOf(user.language_code), source, t, onlyNew ? 1 : 0).run();
}

async function unsubscribe(env, tgId) {
  await env.DB.prepare('UPDATE subscribers SET active = 0, updated_at = ?2 WHERE tg_id = ?1').bind(String(tgId), now()).run();
}

export async function isSubscribed(env, tgId) {
  const s = await env.DB.prepare('SELECT active FROM subscribers WHERE tg_id = ?1').bind(String(tgId)).first();
  return !!s && s.active === 1;
}

// ---------- подписка из игры ----------

/** Кнопка в меню игры: состояние подписки (гость — guest: true, кнопки у него нет). */
export async function botStatus(request, env) {
  const tgId = await tgIdOf(env, await requireToken(request, env));
  return json(tgId ? { subscribed: await isSubscribed(env, tgId) } : { guest: true, subscribed: false });
}

/**
 * Кнопка «Подписаться» / «Отписаться» (игра уже спросила у Telegram разрешение писать — requestWriteAccess).
 * Подписка подтверждается сообщением в чат с ботом: Telegram отказал (403) — писать боту нельзя, подписки нет (no_access).
 */
export async function botSubscribe(request, env) {
  const tgId = await tgIdOf(env, await requireToken(request, env));
  if (!tgId)
    throw new HttpError(403, 'guest');
  const b = await body(request);
  const lang = langOf(b.lang);
  if (!b.on) {
    await unsubscribe(env, tgId);
    return json({ subscribed: false });
  }
  const p = await env.DB.prepare(`SELECT name FROM players WHERE provider = 'telegram' AND ext_id = ?1`).bind(tgId).first();
  await subscribe(env, { id: tgId, first_name: p ? p.name : '', language_code: lang }, 'game');
  const sent = await sendCard(env, tgId, T[lang], true); // подтверждение в чат — та же карточка с кнопкой «Отписаться»
  if (!sent.data.ok && (sent.status === 403 || sent.status === 400)) {
    await unsubscribe(env, tgId);
    return json({ subscribed: false, error: 'no_access' });
  }
  return json({ subscribed: true });
}

// ---------- вебхук ----------

export async function webhook(request, env) {
  // Telegram присылает секрет, заданный в setWebhook — без него запрос не от Telegram
  if (!env.WEBHOOK_SECRET || !timingSafeEqual(request.headers.get('X-Telegram-Bot-Api-Secret-Token') || '', env.WEBHOOK_SECRET))
    throw new HttpError(401, 'bad_secret');
  const update = await body(request);

  // бота заблокировали / разблокировали
  const member = update.my_chat_member;
  if (member && member.chat && member.chat.type === 'private') {
    const status = member.new_chat_member && member.new_chat_member.status;
    if (status === 'kicked' || status === 'left')
      await unsubscribe(env, member.from.id);
    return json({ ok: true });
  }

  const cb = update.callback_query;
  if (cb && cb.from && cb.message && cb.message.chat && cb.message.chat.type === 'private') {
    await callback(env, cb);
    return json({ ok: true });
  }

  const msg = update.message;
  if (msg && msg.chat && msg.chat.type === 'private' && typeof msg.text === 'string' && msg.from)
    await command(env, msg);
  return json({ ok: true }); // Telegram ждёт 200, иначе будет повторять
}

/** Кнопка карточки подписки: переключить и обновить карточку на месте; всплывающая подсказка — итог. */
async function callback(env, cb) {
  const L = T[langOf(cb.from.language_code)];
  if (cb.data !== 'sub:1' && cb.data !== 'sub:0')
    return tg(env, 'answerCallbackQuery', { callback_query_id: cb.id });
  const on = cb.data === 'sub:1';
  if (on) await subscribe(env, cb.from, 'bot');
  else await unsubscribe(env, cb.from.id);
  await tg(env, 'answerCallbackQuery', { callback_query_id: cb.id, text: on ? L.toastOn : L.toastOff });
  const card = subCard(env, L, on);
  await tg(env, 'editMessageText', {
    chat_id: cb.message.chat.id, message_id: cb.message.message_id, text: card.text, parse_mode: 'HTML',
    reply_markup: JSON.parse(card.markup),
  });
}

async function command(env, msg) {
  const user = msg.from;
  const chat = msg.chat.id;
  const L = T[langOf(user.language_code)];
  const text = msg.text.trim();
  const [cmdRaw, ...args] = text.split(/\s+/);
  const cmd = KB_ACTIONS.get(text) || cmdRaw.toLowerCase().replace(/@\w+$/, '');
  const admin = adminIds(env).includes(String(user.id));

  switch (cmd) {
    case '/start': {
      await subscribe(env, user, 'bot');
      const payload = args[0] || '';
      const code = payload.startsWith('room_') ? payload.slice(5).toUpperCase() : '';
      if (ROOM_CODE.test(code))
        return reply(env, chat, L.room(code), keyboard([[{ text: L.joinRoom(code), url: roomUrl(env, code) }]]));
      await reply(env, chat, esc(L.welcome(user.first_name || '')), replyKeyboard(L));
      await sendCard(env, chat, L, true);
      return streamList(env, chat, L, true);
    }
    case '/status': // кнопка «🔔 Подписка»: текущее состояние с переключателем
      return sendCard(env, chat, L, await isSubscribed(env, user.id));
    case '/sub':
    case '/subscribe':
      await subscribe(env, user, 'bot');
      return sendCard(env, chat, L, true);
    case '/room': {
      const code = (args[0] || '').toUpperCase();
      if (!ROOM_CODE.test(code))
        return reply(env, chat, L.roomUsage);
      return reply(env, chat, L.room(code), keyboard([[{ text: L.joinRoom(code), url: roomUrl(env, code) }]]));
    }
    case '/stream':
    case '/streams':
      return streamList(env, chat, L, false);
    case '/stop':
      await unsubscribe(env, user.id);
      return sendCard(env, chat, L, false);
    case '/id':
      return reply(env, chat, L.id(user.id));
  }

  if (!admin)
    return;
  switch (cmd) {
    // /streamer_add <tg_id> <ссылка на канал или -> <имя…>
    case '/streamer_add': {
      const [id, url, ...nameParts] = args;
      const name = cleanName(nameParts.join(' '));
      if (!/^\d+$/.test(id || '') || !url || !name)
        return reply(env, chat, 'Формат: /streamer_add &lt;tg_id&gt; &lt;ссылка на канал или -&gt; &lt;имя&gt;\nID стример узнаёт командой /id');
      const channel = /^https:\/\/\S+$/.test(url) ? url : '';
      await env.DB.prepare(
        `INSERT INTO streamers (tg_id, name, channel_url, added_at) VALUES (?1, ?2, ?3, ?4)
         ON CONFLICT (tg_id) DO UPDATE SET name = excluded.name, channel_url = excluded.channel_url`)
        .bind(id, name, channel, now()).run();
      return reply(env, chat, `Стример добавлен: ${esc(name)} (${id})${channel ? '\n' + esc(channel) : ''}`);
    }
    case '/streamer_del': {
      const r = await env.DB.prepare('DELETE FROM streamers WHERE tg_id = ?1').bind(args[0] || '').run();
      return reply(env, chat, r.meta.changes ? 'Стример удалён' : 'Такого стримера нет');
    }
    case '/streamers': {
      const list = await env.DB.prepare('SELECT tg_id, name, channel_url FROM streamers ORDER BY added_at').all();
      return reply(env, chat, list.results.length
        ? list.results.map(s => `• ${esc(s.name)} — <code>${s.tg_id}</code> ${esc(s.channel_url)}`).join('\n')
        : 'Стримеров пока нет. /streamer_add &lt;tg_id&gt; &lt;ссылка&gt; &lt;имя&gt;');
    }
    case '/stats':
      return reply(env, chat, await statsText(env));
    // /broadcast <текст> — всем активным подписчикам (с кнопкой «Играть»)
    case '/broadcast': {
      const text = msg.text.trim().slice(cmdRaw.length).trim();
      if (!text)
        return reply(env, chat, 'Формат: /broadcast текст');
      const n = await enqueueAll(env, 'broadcast', () => esc(text), lang => keyboard([[{ text: T[lang].play, url: appUrl(env) }]]));
      return reply(env, chat, `Поставлено в очередь: ${n}`);
    }
  }
}

// ---------- /stats ----------

// колонки: сегодня, 7 дней, 30 дней (сутки по Москве), всё время; ?1..?3 — начала периодов
const periodSums = col => `SUM(${col} >= ?1) AS p0, SUM(${col} >= ?2) AS p1, SUM(${col} >= ?3) AS p2, COUNT(*) AS p3`;
const periodDistinct = (col, id) => [1, 2, 3].map(i => `COUNT(DISTINCT CASE WHEN ${col} >= ?${i} THEN ${id} END) AS p${i - 1}`).join(', ') +
  `, COUNT(DISTINCT ${id}) AS p3`;
const periods = row => [0, 1, 2, 3].map(i => row[`p${i}`] || 0).join(' / ');

async function statsText(env) {
  const t = now();
  const since = [0, 6, 29].map(back => statDayStart(t, back));
  const q = (sql, ...args) => env.DB.prepare(sql).bind(...args);
  const [visits, fresh, played, matches, multi, platforms, subs, outbox] = (await env.DB.batch([
    q(`SELECT ${periodDistinct('day', 'player_id')} FROM player_days`, ...since.map(statDay)),
    q(`SELECT ${periodSums('created_at')} FROM players`, ...since),
    q(`SELECT ${periodDistinct('t', 'p')} FROM (SELECT host_id AS p, created_at AS t FROM matches
         UNION ALL SELECT player_id, created_at FROM match_reports)`, ...since),
    q(`SELECT ${periodSums('created_at')} FROM matches`, ...since),
    // хотя бы два отчёта — играли живые люди, а не один игрок с ботами
    q(`SELECT ${periodSums('t')} FROM (SELECT MIN(created_at) AS t FROM match_reports GROUP BY match_id HAVING COUNT(*) >= 2)`, ...since),
    q('SELECT platform, COUNT(*) AS n FROM matches WHERE created_at >= ?1 GROUP BY platform ORDER BY n DESC', since[2]),
    q('SELECT COUNT(*) AS n, SUM(active) AS a FROM subscribers'),
    q('SELECT COUNT(*) AS n FROM outbox'),
  ])).map(r => r.results);
  // редактор и dev-сборки (RatingApi.MatchPlatform) — тесты, а не площадка
  const isTest = p => p === 'editor' || /-dev$/.test(p);
  const tests = platforms.filter(p => isTest(p.platform)).reduce((n, p) => n + p.n, 0);
  const byPlatform = [
    ...platforms.filter(p => !isTest(p.platform)).map(p => `${esc(p.platform || '?')} ${p.n}`),
    ...(tests ? [`тесты (редактор, dev-сборки) ${tests}`] : []),
  ].join(' · ') || '—';
  return [
    '📊 <b>Статистика</b>: сегодня / 7 дней / 30 дней / всего (сутки по Москве)',
    '',
    `👥 Заходили в игру: ${periods(visits[0])}`,
    `🆕 Новых игроков: ${periods(fresh[0])}`,
    `🎮 Сыграли матч: ${periods(played[0])}`,
    `⚔️ Матчей: ${periods(matches[0])}`,
    `🤝 Из них с живыми соперниками: ${periods(multi[0])}`,
    `🖥 Матчи по площадкам за 30 дней: ${byPlatform}`,
    '',
    `Подписчиков: ${subs[0].a || 0} активных из ${subs[0].n}`,
    `В очереди на отправку: ${outbox[0].n}`,
  ].join('\n');
}

async function streamList(env, chat, L, silentIfEmpty) {
  const live = await liveStreams(env);
  if (!live.length)
    return silentIfEmpty ? null : reply(env, chat, L.noStreams);
  const rows = [];
  for (const s of live) {
    rows.push([{ text: L.joinStream(s.name), url: roomUrl(env, s.room_code) }]);
    if (s.channel_url)
      rows.push([{ text: `${L.watch}: ${s.name}`, url: s.channel_url }]);
  }
  return reply(env, chat, L.streams, keyboard(rows));
}

async function liveStreams(env) {
  const t = now();
  const r = await env.DB.prepare(
    `SELECT s.room_code, st.name, st.channel_url FROM streams s JOIN streamers st ON st.tg_id = s.streamer_tg
     WHERE s.ended_at IS NULL AND s.last_seen > ?1 ORDER BY s.started_at DESC LIMIT 5`)
    .bind(t - STREAM_IDLE_SEC).all();
  return r.results;
}

// ---------- игра стримера ----------

/** Telegram ID вошедшего игрока (гость — null). */
async function tgIdOf(env, me) {
  if (me.v !== 1)
    return null;
  const p = await env.DB.prepare(`SELECT ext_id FROM players WHERE id = ?1 AND provider = 'telegram'`).bind(me.p).first();
  return p ? p.ext_id : null;
}

async function streamerOf(env, me) {
  const tgId = await tgIdOf(env, me);
  return tgId ? await env.DB.prepare('SELECT * FROM streamers WHERE tg_id = ?1').bind(tgId).first() : null;
}

async function openStreamOf(env, tgId) {
  return await env.DB.prepare('SELECT * FROM streams WHERE streamer_tg = ?1 AND ended_at IS NULL ORDER BY started_at DESC LIMIT 1')
    .bind(tgId).first();
}

function streamState(stream) {
  const t = now();
  return {
    partner: true,
    streamId: stream.id,
    announcesLeft: Math.max(0, MAX_ANNOUNCES - stream.announces),
    nextAnnounceIn: Math.max(0, stream.last_announce + ANNOUNCE_GAP_SEC - t),
  };
}

/**
 * Стример создал стрим-комнату (и потом раз в несколько минут — «я ещё тут»). Не партнёр — {partner:false}:
 * комната работает, просто без анонсов и наград. Тот же стример с другой комнатой — стрим продолжается с новым кодом.
 */
export async function streamOpen(request, env) {
  const me = await requireToken(request, env);
  const { roomCode } = await body(request);
  const code = String(roomCode || '').toUpperCase();
  if (!ROOM_CODE.test(code))
    throw new HttpError(400, 'bad_room_code');
  const streamer = await streamerOf(env, me);
  if (!streamer)
    return json({ partner: false });

  const t = now();
  let stream = await openStreamOf(env, streamer.tg_id);
  if (stream && t - stream.last_seen > STREAM_IDLE_SEC) {
    await endStream(env, stream.id); // прошлый стрим давно пропал — это уже новый
    stream = null;
  }
  if (stream) {
    await env.DB.prepare('UPDATE streams SET room_code = ?1, last_seen = ?2 WHERE id = ?3').bind(code, t, stream.id).run();
    stream.room_code = code;
  } else {
    stream = { id: randomId(), streamer_tg: streamer.tg_id, room_code: code, started_at: t, last_seen: t, announces: 0, last_announce: 0 };
    await env.DB.prepare('INSERT INTO streams (id, streamer_tg, room_code, started_at, last_seen) VALUES (?1, ?2, ?3, ?4, ?4)')
      .bind(stream.id, streamer.tg_id, code, t).run();
  }
  return json({ ...streamState(stream), name: streamer.name });
}

/** Анонс всем подписчикам: kind = now («играю прямо сейчас») или soon («через 15 минут»). */
export async function streamAnnounce(request, env, ctx) {
  const me = await requireToken(request, env);
  const { kind } = await body(request);
  const streamer = await streamerOf(env, me);
  if (!streamer)
    throw new HttpError(403, 'not_partner');
  const stream = await openStreamOf(env, streamer.tg_id);
  if (!stream)
    throw new HttpError(404, 'no_stream');
  const t = now();
  if (stream.announces >= MAX_ANNOUNCES)
    throw new HttpError(429, 'announce_limit');
  if (t - stream.last_announce < ANNOUNCE_GAP_SEC)
    throw new HttpError(429, 'announce_too_soon');

  // замок: второй одновременный запрос не разошлёт повторно
  const claim = await env.DB.prepare(
    'UPDATE streams SET announces = announces + 1, last_announce = ?1 WHERE id = ?2 AND announces = ?3 AND last_announce = ?4')
    .bind(t, stream.id, stream.announces, stream.last_announce).run();
  if (!claim.meta.changes)
    throw new HttpError(429, 'announce_too_soon');

  const soon = kind === 'soon';
  const queued = await enqueueAll(env, 'announce',
    lang => soon ? T[lang].announceSoon(streamer.name) : T[lang].announceNow(streamer.name, !!streamer.channel_url),
    lang => keyboard([
      [{ text: T[lang].joinRoom(stream.room_code), url: roomUrl(env, stream.room_code) }],
      streamer.channel_url ? [{ text: T[lang].watch, url: streamer.channel_url }] : [],
    ]),
    streamer.tg_id);
  ctx.waitUntil(drainOutbox(env)); // первая пачка — сразу, остальное — cron раз в минуту
  stream.announces++;
  stream.last_announce = t;
  return json({ ...streamState(stream), queued });
}

/** Стример вышел из стрим-комнаты: стрим закончен, игрокам — итоги. */
export async function streamClose(request, env, ctx) {
  const me = await requireToken(request, env);
  const tgId = await tgIdOf(env, me);
  const stream = tgId ? await openStreamOf(env, tgId) : null;
  if (stream) {
    await endStream(env, stream.id);
    ctx.waitUntil(sendResults(env).then(() => drainOutbox(env)));
  }
  return json({ ok: true });
}

/** Игрок доиграл матч в стрим-комнате: место на пьедестале (1 — лучший) и победила ли его команда. */
export async function streamPlayed(request, env) {
  const me = await requireToken(request, env);
  const b = await body(request);
  const code = String(b.roomCode || '').toUpperCase();
  const place = Number(b.place);
  if (!ROOM_CODE.test(code) || !Number.isInteger(place) || place < 1 || place > MAX_PLACE)
    throw new HttpError(400, 'bad_request');
  const tgId = await tgIdOf(env, me);
  if (!tgId)
    return json({ status: 'guest' }); // итоги и награда — аккаунтам Telegram
  const t = now();
  const stream = await env.DB.prepare(
    'SELECT id, streamer_tg FROM streams WHERE room_code = ?1 AND ended_at IS NULL AND last_seen > ?2 ORDER BY started_at DESC LIMIT 1')
    .bind(code, t - STREAM_IDLE_SEC).first();
  if (!stream || stream.streamer_tg === tgId)
    return json({ status: 'no_stream' });

  const won = b.won ? 1 : 0;
  await env.DB.prepare(
    `INSERT INTO stream_players (stream_id, player_id, tg_id, matches, wins, best_place, updated_at) VALUES (?1, ?2, ?3, 1, ?4, ?5, ?6)
     ON CONFLICT (stream_id, player_id) DO UPDATE SET
       matches = matches + 1, wins = wins + excluded.wins, best_place = MIN(best_place, excluded.best_place), updated_at = excluded.updated_at
     WHERE stream_players.updated_at < ?7`)
    .bind(stream.id, me.p, tgId, won, place, t, t - PLAYED_GAP_SEC).run();
  return json({ status: 'ok' });
}

async function endStream(env, id) {
  await env.DB.prepare('UPDATE streams SET ended_at = ?1 WHERE id = ?2 AND ended_at IS NULL').bind(now(), id).run();
}

// ---------- итоги и награды ----------

/** Закончившиеся стримы: каждому сыгравшему — сообщение с итогами и скин «Зритель стрима». */
export async function sendResults(env) {
  const ended = await env.DB.prepare(
    `SELECT s.id, st.name FROM streams s JOIN streamers st ON st.tg_id = s.streamer_tg
     WHERE s.ended_at IS NOT NULL AND s.results_sent = 0 LIMIT 5`).all();
  for (const stream of ended.results) {
    const claim = await env.DB.prepare('UPDATE streams SET results_sent = 1 WHERE id = ?1 AND results_sent = 0').bind(stream.id).run();
    if (!claim.meta.changes)
      continue;
    const players = await env.DB.prepare(
      `SELECT p.player_id, p.tg_id, p.matches, p.wins, p.best_place, COALESCE(sub.lang, 'ru') AS lang
       FROM stream_players p LEFT JOIN subscribers sub ON sub.tg_id = p.tg_id WHERE p.stream_id = ?1`)
      .bind(stream.id).all();
    const t = now();
    const statements = [];
    for (const p of players.results) {
      const skin = await env.DB.prepare('INSERT OR IGNORE INTO player_skins (player_id, skin, source, created_at) VALUES (?1, ?2, ?3, ?4)')
        .bind(p.player_id, STREAM_SKIN, 'stream:' + stream.id, t).run();
      const L = T[p.lang] || T.ru;
      // пишем, даже если игрок не подписан: бот может писать тем, кто открывал Mini App с разрешением; нет — Telegram откажет
      statements.push(env.DB.prepare('INSERT INTO outbox (chat_id, text, markup, kind, created_at) VALUES (?1, ?2, ?3, ?4, ?5)')
        .bind(p.tg_id, L.results(stream.name, p, skin.meta.changes > 0), keyboard([[{ text: L.play, url: appUrl(env) }]]), 'results', t));
    }
    if (statements.length)
      await env.DB.batch(statements);
  }
}

/** Скины-награды аккаунта (для ответа на вход). */
export async function skinsOf(env, playerId) {
  const r = await env.DB.prepare('SELECT skin FROM player_skins WHERE player_id = ?1 ORDER BY skin').bind(playerId).all();
  return r.results.map(x => x.skin);
}

// ---------- рассылка ----------

/** Поставить сообщение всем активным подписчикам (кроме except): по языкам, одним запросом на язык. */
async function enqueueAll(env, kind, textFor, markupFor, except = '') {
  const t = now();
  let total = 0;
  for (const lang of Object.keys(T)) {
    const r = await env.DB.prepare(
      `INSERT INTO outbox (chat_id, text, markup, kind, created_at)
       SELECT tg_id, ?1, ?2, ?3, ?4 FROM subscribers WHERE active = 1 AND lang = ?5 AND tg_id <> ?6`)
      .bind(textFor(lang), markupFor(lang), kind, t, lang, except).run();
    total += r.meta.changes || 0;
  }
  return total;
}

/** Отправить пачку из outbox. 403/400 — подписчик заблокировал бота или чата нет: отписываем. 429 — хватит до следующего раза. */
export async function drainOutbox(env, limit) {
  const batch = Math.max(1, Math.min(limit || Number(env.OUTBOX_BATCH) || DEFAULT_BATCH, 1000));
  const rows = await env.DB.prepare('SELECT * FROM outbox ORDER BY id LIMIT ?1').bind(batch).all();
  let sent = 0;
  for (const m of rows.results) {
    // строку забираем заранее: параллельный запуск (cron + анонс) не отправит её второй раз
    const claim = await env.DB.prepare('DELETE FROM outbox WHERE id = ?1').bind(m.id).run();
    if (!claim.meta.changes)
      continue;
    const r = await reply(env, m.chat_id, m.text, m.markup);
    if (r.data.ok) {
      sent++;
      continue;
    }
    if (r.status === 403 || r.status === 400) {
      await unsubscribe(env, m.chat_id);
      continue;
    }
    // 429 и сбои: вернуть в очередь (попыток не больше MAX_ATTEMPTS)
    if (m.attempts + 1 < MAX_ATTEMPTS)
      await env.DB.prepare('INSERT INTO outbox (id, chat_id, text, markup, kind, attempts, created_at) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)')
        .bind(m.id, m.chat_id, m.text, m.markup, m.kind, m.attempts + 1, m.created_at).run();
    if (r.status === 429)
      break;
  }
  return sent;
}

/** Cron раз в минуту: пропавшие стримы закрыть, итоги поставить в очередь, отправить пачку. */
export async function scheduled(env) {
  const t = now();
  await env.DB.prepare('UPDATE streams SET ended_at = ?1 WHERE ended_at IS NULL AND (last_seen < ?2 OR started_at < ?3)')
    .bind(t, t - STREAM_IDLE_SEC, t - STREAM_MAX_SEC).run();
  await sendResults(env);
  await drainOutbox(env);
}

/**
 * Настройка бота (deploy.ps1): вебхук на этот Worker с секретом и список команд в меню.
 * Защищена тем же секретом вебхука — его знает только тот, кто выкладывал.
 */
export async function setup(request, env, url) {
  if (!env.WEBHOOK_SECRET || !timingSafeEqual(request.headers.get('X-Setup-Key') || '', env.WEBHOOK_SECRET))
    throw new HttpError(401, 'bad_secret');
  const hook = await tg(env, 'setWebhook', {
    url: `${url.origin}/tg/webhook`,
    secret_token: env.WEBHOOK_SECRET,
    allowed_updates: ['message', 'my_chat_member', 'callback_query'],
  });
  const commands = (lang, list) => tg(env, 'setMyCommands', { commands: list, ...(lang ? { language_code: lang } : {}) });
  const ru = [
    { command: 'stream', description: 'Кто сейчас стримит' },
    { command: 'room', description: 'Войти в комнату по коду' },
    { command: 'sub', description: 'Подписаться на анонсы' },
    { command: 'stop', description: 'Отписаться от анонсов' },
  ];
  const en = [
    { command: 'stream', description: 'Who is streaming now' },
    { command: 'room', description: 'Join a room by code' },
    { command: 'sub', description: 'Subscribe to announcements' },
    { command: 'stop', description: 'Unsubscribe from announcements' },
  ];
  const cmd = await commands('', en);
  const cmdRu = await commands('ru', ru);
  return json({ webhook: hook.data, commands: cmd.data.ok && cmdRu.data.ok });
}
