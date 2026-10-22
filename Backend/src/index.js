// Rocket Wars — сервер рейтинга (Cloudflare Worker + D1).
// Хост в комнате — обычный игрок, поэтому одному его слову не верим: итог матча засчитывается, только когда
// отчёты нескольких настоящих игроков (аккаунты площадки) совпали. Подробности — Backend/README.md.

import { CORS, HttpError, json, now, body, requireToken, readToken, signToken, cleanName, randomId, hmac, sha256, hex, timingSafeEqual } from './common.js';
import * as bot from './bot.js';

const WINS_NEEDED = 4;              // MatchManager.WinsNeeded; стрим-комната — до 1–4 побед (поле wins отчёта)
const MIN_MATCH_SEC = 60;           // матч до 4 побед короче не бывает (до меньшего — пропорционально)
const MIN_ROUND_SEC = 12;           // отсчёт 3 с + итог раунда 4 с + сам раунд
const MATCH_TTL_SEC = 3 * 3600;     // отчёты по старому матчу не принимаем
const TOKEN_TTL_SEC = 7 * 86400;
const TELEGRAM_AUTH_TTL_SEC = 86400;
const MAX_ROWS = 16;                // NetworkBootstrap.MaxPlayers + боты
const MAX_STAT = 200;
const PRACTICE_POINTS = 3;          // матч с ботами без второго живого игрока
const PRACTICE_WEEK_CAP = 30;
const TOP_LIMIT = 100;


export default {
  async fetch(request, env, ctx) {
    if (request.method === 'OPTIONS')
      return new Response(null, { status: 204, headers: CORS });
    try {
      const url = new URL(request.url);
      const route = `${request.method} ${url.pathname}`;
      switch (route) {
        case 'GET /v1/health': return json({ ok: true });
        case 'POST /v1/auth': return await auth(request, env);
        case 'POST /v1/match/start': return await matchStart(request, env);
        case 'POST /v1/match/report': return await matchReport(request, env);
        case 'GET /v1/match/result': return await matchResult(request, env, url);
        case 'GET /v1/leaderboard': return await leaderboard(request, env, url);
        case 'POST /v1/stream/open': return await bot.streamOpen(request, env);
        case 'POST /v1/stream/announce': return await bot.streamAnnounce(request, env, ctx);
        case 'POST /v1/stream/close': return await bot.streamClose(request, env, ctx);
        case 'POST /v1/stream/played': return await bot.streamPlayed(request, env);
        case 'POST /tg/webhook': return await bot.webhook(request, env);
        case 'POST /tg/setup': return await bot.setup(request, env, url);
        default: return json({ error: 'not_found' }, 404);
      }
    } catch (e) {
      if (e instanceof HttpError)
        return json({ error: e.code }, e.status);
      console.error(e);
      return json({ error: 'internal' }, 500);
    }
  },

  // cron (wrangler.toml, раз в минуту): рассылка бота пачками, итоги закончившихся стримов
  async scheduled(event, env, ctx) {
    ctx.waitUntil(bot.scheduled(env));
  },
};

/** Гости в общем рейтинге — только при локальной проверке (GUESTS_RANKED=1 в .dev.vars). */
const guestsRanked = env => env.GUESTS_RANKED === '1';

// ---------- вход ----------

async function auth(request, env) {
  const { provider, data } = await body(request);
  let extId, name = '', verified = 0;
  if (provider === 'telegram') {
    const user = await verifyTelegram(String(data || ''), env.BOT_TOKEN);
    extId = String(user.id);
    name = user.first_name || user.username || '';
    verified = 1;
    // разрешил боту писать (запрос в Mini App) — будет получать анонсы стримов и итоги
    if (user.allows_write_to_pm)
      await bot.subscribe(env, user, 'webapp');
  } else if (provider === 'guest') {
    // ключ устройства: случайные 32+ hex-символа, на сервере храним только хэш
    if (typeof data !== 'string' || !/^[0-9a-f]{32,64}$/.test(data))
      throw new HttpError(400, 'bad_guest_key');
    extId = await sha256(data);
  } else {
    throw new HttpError(400, 'bad_provider');
  }

  name = cleanName(name);
  const t = now();
  await env.DB.prepare(
    `INSERT INTO players (provider, ext_id, name, verified, created_at) VALUES (?1, ?2, ?3, ?4, ?5)
     ON CONFLICT (provider, ext_id) DO UPDATE SET verified = excluded.verified`)
    .bind(provider, extId, name, verified, t).run();
  const player = await env.DB.prepare('SELECT id, name, verified FROM players WHERE provider = ?1 AND ext_id = ?2')
    .bind(provider, extId).first();
  const token = await signToken({ p: player.id, v: player.verified, e: t + TOKEN_TTL_SEC }, env.TOKEN_SECRET);
  return json({
    token, playerId: player.id, verified: player.verified === 1, ranked: isRanked(player.verified, env),
    skins: await bot.skinsOf(env, player.id), // скины-награды (стримы)
  });
}

const isRanked = (verified, env) => verified === 1 || guestsRanked(env);

/** https://core.telegram.org/bots/webapps#validating-data-received-via-the-mini-app */
async function verifyTelegram(initData, botToken) {
  if (!botToken)
    throw new HttpError(500, 'no_bot_token');
  const params = new URLSearchParams(initData);
  const hash = params.get('hash');
  if (!hash)
    throw new HttpError(401, 'bad_init_data');
  params.delete('hash');
  const check = [...params.entries()].map(([k, v]) => `${k}=${v}`).sort().join('\n');
  const secret = await hmac(new TextEncoder().encode('WebAppData'), botToken);
  const expected = hex(await hmac(secret, check));
  if (!timingSafeEqual(expected, hash))
    throw new HttpError(401, 'bad_init_data');
  const authDate = Number(params.get('auth_date') || 0);
  if (now() - authDate > TELEGRAM_AUTH_TTL_SEC)
    throw new HttpError(401, 'init_data_expired');
  let user;
  try { user = JSON.parse(params.get('user') || ''); } catch { }
  if (!user || !user.id)
    throw new HttpError(401, 'bad_init_data');
  return user;
}

// ---------- матч ----------

async function matchStart(request, env) {
  const me = await requireToken(request, env);
  const { platform } = await body(request);
  const t = now();
  const recent = await env.DB.prepare('SELECT COUNT(*) AS n FROM matches WHERE host_id = ?1 AND created_at > ?2')
    .bind(me.p, t - 60).first();
  if (recent.n >= 3)
    throw new HttpError(429, 'too_many_matches');
  const id = randomId();
  await env.DB.prepare('INSERT INTO matches (id, host_id, platform, created_at) VALUES (?1, ?2, ?3, ?4)')
    .bind(id, me.p, String(platform || '').slice(0, 16), t).run();
  return json({ matchId: id });
}

/**
 * Отчёт игрока в конце матча: вся таблица итогов (как её видит он) и id его строки.
 * Таблицы приводятся к одному виду и сравниваются по хэшу. Итог принят, когда совпали отчёты
 * max(2, половины) живых игроков с аккаунтами площадки; очки получают те, кто прислал отчёт.
 */
async function matchReport(request, env) {
  const me = await requireToken(request, env);
  const report = normalizeReport(await body(request));
  const t = now();

  const match = await env.DB.prepare('SELECT * FROM matches WHERE id = ?1').bind(report.matchId).first();
  if (!match)
    throw new HttpError(404, 'no_match');
  const age = t - match.created_at;
  if (age > MATCH_TTL_SEC)
    throw new HttpError(410, 'match_expired');
  const rounds = report.score0 + report.score1;
  if (age < Math.max(MIN_MATCH_SEC * report.wins / WINS_NEEDED, rounds * MIN_ROUND_SEC) && env.SKIP_DURATION_CHECK !== '1')
    throw new HttpError(400, 'match_too_short');

  const mine = report.rows.find(r => r.id === report.me);
  if (!mine || mine.bot)
    throw new HttpError(400, 'bad_row');

  const hash = await sha256(canonical(report));
  const verified = isRanked(me.v, env) ? 1 : 0;
  const inserted = await env.DB.prepare(
    `INSERT OR IGNORE INTO match_reports (match_id, player_id, row_id, hash, verified, report, created_at)
     VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)`)
    .bind(report.matchId, me.p, report.me, hash, verified, JSON.stringify(report), t).run();
  if (!inserted.meta.changes) {
    // повтор того же отчёта (переотправка после обрыва) — просто вернуть состояние
    const prev = await env.DB.prepare('SELECT row_id, hash FROM match_reports WHERE match_id = ?1 AND player_id = ?2')
      .bind(report.matchId, me.p).first();
    if (!prev || prev.row_id !== report.me || prev.hash !== hash)
      throw new HttpError(409, 'duplicate_report');
  }

  await tryFinalize(env, match, report, hash);
  return json(await creditStatus(env, report.matchId, me.p, hash));
}

async function tryFinalize(env, match, report, hash) {
  const humans = report.rows.filter(r => !r.bot).length;
  let finalHash = match.final_hash;
  if (!finalHash) {
    let accepted = false;
    if (humans >= 2) {
      const quorum = Math.max(2, Math.ceil(humans / 2));
      const agree = await env.DB.prepare('SELECT COUNT(*) AS n FROM match_reports WHERE match_id = ?1 AND hash = ?2 AND verified = 1')
        .bind(match.id, hash).first();
      accepted = agree.n >= quorum;
    } else {
      accepted = true; // тренировка с ботами: подтверждать некому, очков мало и они ограничены за неделю
    }
    if (!accepted)
      return;
    // кто первый набрал кворум, тот и итог; гонка двух запросов решается условием final_hash IS NULL
    await env.DB.prepare('UPDATE matches SET final_hash = ?1, finalized_at = ?2 WHERE id = ?3 AND final_hash IS NULL')
      .bind(hash, now(), match.id).run();
    finalHash = (await env.DB.prepare('SELECT final_hash FROM matches WHERE id = ?1').bind(match.id).first()).final_hash;
  }
  if (finalHash !== hash)
    return;

  // начислить всем, кто прислал совпавший отчёт и ещё не получил очки
  const reporters = await env.DB.prepare(
    `SELECT r.player_id, r.row_id, r.verified FROM match_reports r
     LEFT JOIN match_credits c ON c.match_id = r.match_id AND c.player_id = r.player_id
     WHERE r.match_id = ?1 AND r.hash = ?2 AND c.player_id IS NULL`)
    .bind(match.id, hash).all();
  for (const r of reporters.results) {
    const row = report.rows.find(x => x.id === r.row_id);
    if (row)
      await credit(env, match.id, r.player_id, r.verified === 1, row, report.winner, humans < 2, report.wins);
  }
}

/** Очки за матч до 4 побед; короткий матч стрим-комнаты (до 1–3) — пропорционально меньше. */
function matchPoints(row, winner, wins = WINS_NEEDED) {
  return Math.round(fullMatchPoints(row, winner) * wins / WINS_NEEDED);
}

function fullMatchPoints(row, winner) {
  if (row.left)
    return 0;
  let p = 10;
  if (row.team === winner) p += 20;
  p += 2 * Math.min(row.downed, 15);
  p += 2 * Math.min(row.revives, 10);
  p += 3 * Math.min(row.silos, 10);
  p += 5 * popcount(row.ach);
  p -= 2 * Math.min(row.tk, 10);
  return Math.max(0, p);
}

async function credit(env, matchId, playerId, ranked, row, winner, practice, wins) {
  const season = weekId(new Date());
  const t = now();
  const week = await env.DB.prepare('SELECT points, practice_points FROM scores WHERE player_id = ?1 AND season = ?2')
    .bind(playerId, season).first();
  let points;
  if (practice)
    points = row.left ? 0 : Math.max(0, Math.min(PRACTICE_POINTS, PRACTICE_WEEK_CAP - (week ? week.practice_points : 0)));
  else
    points = matchPoints(row, winner, wins);

  const rankBefore = ranked && week && week.points > 0 ? await rankOf(env, season, week.points) : null;

  // строка начисления — замок: второй параллельный запрос не начислит повторно
  const claim = await env.DB.prepare(
    `INSERT OR IGNORE INTO match_credits (match_id, player_id, season, points, practice, created_at) VALUES (?1, ?2, ?3, ?4, ?5, ?6)`)
    .bind(matchId, playerId, season, points, practice ? 1 : 0, t).run();
  if (!claim.meta.changes)
    return;

  const won = !row.left && row.team === winner ? 1 : 0;
  const upsert = env.DB.prepare(
    `INSERT INTO scores (player_id, season, points, matches, wins, kills, practice_points, updated_at)
     VALUES (?1, ?2, ?3, 1, ?4, ?5, ?6, ?7)
     ON CONFLICT (player_id, season) DO UPDATE SET
       points = points + excluded.points, matches = matches + 1, wins = wins + excluded.wins,
       kills = kills + excluded.kills, practice_points = practice_points + excluded.practice_points,
       updated_at = excluded.updated_at`);
  const practicePoints = practice ? points : 0;
  await env.DB.batch([
    upsert.bind(playerId, season, points, won, row.downed, practicePoints, t),
    upsert.bind(playerId, 'all', points, won, row.downed, practicePoints, t),
    // в таблице — то имя, под которым игрока видели в матче
    env.DB.prepare('UPDATE players SET name = ?1 WHERE id = ?2 AND ?1 <> \'\'').bind(row.name, playerId),
  ]);

  if (ranked) {
    const after = await env.DB.prepare('SELECT points FROM scores WHERE player_id = ?1 AND season = ?2').bind(playerId, season).first();
    const rankAfter = after.points > 0 ? await rankOf(env, season, after.points) : null;
    await env.DB.prepare('UPDATE match_credits SET rank_before = ?1, rank_after = ?2 WHERE match_id = ?3 AND player_id = ?4')
      .bind(rankBefore, rankAfter, matchId, playerId).run();
  }
}

async function creditStatus(env, matchId, playerId, hash) {
  const c = await env.DB.prepare('SELECT * FROM match_credits WHERE match_id = ?1 AND player_id = ?2').bind(matchId, playerId).first();
  if (c) {
    const week = await env.DB.prepare('SELECT points FROM scores WHERE player_id = ?1 AND season = ?2').bind(playerId, c.season).first();
    return {
      status: 'credited', points: c.points, practice: c.practice === 1,
      weekPoints: week ? week.points : 0, rank: c.rank_after || 0, rankBefore: c.rank_before || 0,
    };
  }
  const m = await env.DB.prepare('SELECT final_hash FROM matches WHERE id = ?1').bind(matchId).first();
  if (m && m.final_hash && hash && m.final_hash !== hash)
    return { status: 'rejected' }; // принят другой вариант таблицы — этот отчёт с ним не совпал
  return { status: 'pending' };
}

async function matchResult(request, env, url) {
  const me = await requireToken(request, env);
  const matchId = url.searchParams.get('matchId') || '';
  const r = await env.DB.prepare('SELECT hash FROM match_reports WHERE match_id = ?1 AND player_id = ?2').bind(matchId, me.p).first();
  if (!r)
    throw new HttpError(404, 'no_report');
  return json(await creditStatus(env, matchId, me.p, r.hash));
}

/** Отчёт клиента → строгий вид (лишние поля отброшены, числа ограничены). */
function normalizeReport(b) {
  if (!b || typeof b.matchId !== 'string' || !/^[0-9a-f]{20}$/.test(b.matchId))
    throw new HttpError(400, 'bad_match_id');
  const int = (v, max) => {
    const n = Number(v);
    if (!Number.isInteger(n) || n < 0 || n > max)
      throw new HttpError(400, 'bad_number');
    return n;
  };
  if (!Array.isArray(b.rows) || b.rows.length === 0 || b.rows.length > MAX_ROWS)
    throw new HttpError(400, 'bad_rows');
  const rows = b.rows.map(r => ({
    id: int(r.id, Number.MAX_SAFE_INTEGER),
    team: int(r.team, 1),
    name: cleanName(r.name),
    downed: int(r.downed, MAX_STAT),
    deaths: int(r.deaths, MAX_STAT),
    revives: int(r.revives, MAX_STAT),
    silos: int(r.silos, MAX_STAT),
    falls: int(r.falls, MAX_STAT),
    tk: int(r.tk, MAX_STAT),
    ach: int(r.ach, 0xFFFFFFFF),
    left: !!r.left,
    bot: !!r.bot,
  }));
  if (new Set(rows.map(r => r.id)).size !== rows.length)
    throw new HttpError(400, 'bad_rows');
  // до скольких побед шёл матч: старые клиенты поле не шлют — 4
  const wins = b.wins === undefined ? WINS_NEEDED : int(b.wins, WINS_NEEDED);
  if (wins < 1)
    throw new HttpError(400, 'bad_score');
  const score0 = int(b.score0, wins), score1 = int(b.score1, wins), winner = int(b.winner, 1);
  if ((winner === 0 ? score0 : score1) !== wins || (winner === 0 ? score1 : score0) >= wins)
    throw new HttpError(400, 'bad_score');
  return { matchId: b.matchId, me: int(b.me, Number.MAX_SAFE_INTEGER), wins, score0, score1, winner, rows };
}

/** Одинаковая таблица у всех игроков → одинаковая строка (порядок строк и полей фиксирован). */
function canonical(r) {
  const rows = [...r.rows].sort((a, b) => a.id - b.id)
    .map(x => [x.id, x.team, x.name, x.downed, x.deaths, x.revives, x.silos, x.falls, x.tk, x.ach, x.left ? 1 : 0, x.bot ? 1 : 0]);
  return JSON.stringify([r.matchId, r.wins, r.score0, r.score1, r.winner, rows]);
}

// ---------- рейтинг ----------

async function leaderboard(request, env, url) {
  const me = await readToken(request, env); // без входа — просто таблица
  const season = url.searchParams.get('season') === 'all' ? 'all' : weekId(new Date());
  const limit = Math.min(TOP_LIMIT, Math.max(1, Number(url.searchParams.get('limit')) || 50));
  const ranked = guestsRanked(env) ? 1 : 0;

  const top = await env.DB.prepare(
    `SELECT s.player_id, p.name, s.points, s.matches, s.wins FROM scores s JOIN players p ON p.id = s.player_id
     WHERE s.season = ?1 AND s.points > 0 AND (p.verified = 1 OR ?2 = 1)
     ORDER BY s.points DESC, s.updated_at ASC LIMIT ?3`)
    .bind(season, ranked, limit).all();
  // одинаковые очки — одно место (1, 2, 2, 4)
  let rank = 0, prev = null;
  const rows = top.results.map((r, i) => {
    if (r.points !== prev) { rank = i + 1; prev = r.points; }
    return { rank, name: r.name, points: r.points, matches: r.matches, wins: r.wins, me: !!me && r.player_id === me.p };
  });

  let mine = null;
  if (me) {
    const s = await env.DB.prepare('SELECT points, matches, wins FROM scores WHERE player_id = ?1 AND season = ?2').bind(me.p, season).first();
    const p = await env.DB.prepare('SELECT name FROM players WHERE id = ?1').bind(me.p).first();
    const ranked = isRanked(me.v, env);
    mine = {
      name: p ? p.name : '', points: s ? s.points : 0, matches: s ? s.matches : 0, wins: s ? s.wins : 0,
      ranked, rank: ranked && s && s.points > 0 ? await rankOf(env, season, s.points) : 0,
    };
  }
  // числа вместо null и без поля me у анонима: клиент читает ответ через JsonUtility
  const out = { season, resetsIn: season === 'all' ? 0 : secondsToNextWeek(new Date()), top: rows };
  if (mine) out.me = mine;
  return json(out);
}

async function rankOf(env, season, points) {
  const r = await env.DB.prepare(
    `SELECT COUNT(*) AS n FROM scores s JOIN players p ON p.id = s.player_id
     WHERE s.season = ?1 AND s.points > ?2 AND (p.verified = 1 OR ?3 = 1)`)
    .bind(season, points, guestsRanked(env) ? 1 : 0).first();
  return r.n + 1;
}

/** Неделя ISO 8601 по UTC: '2026-W43'. */
export function weekId(date) {
  const d = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate()));
  const day = d.getUTCDay() || 7;
  d.setUTCDate(d.getUTCDate() + 4 - day); // четверг этой недели определяет год
  const yearStart = new Date(Date.UTC(d.getUTCFullYear(), 0, 1));
  const week = Math.ceil(((d - yearStart) / 86400000 + 1) / 7);
  return `${d.getUTCFullYear()}-W${String(week).padStart(2, '0')}`;
}

function secondsToNextWeek(date) {
  const day = date.getUTCDay() || 7;
  const next = Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate() + 8 - day);
  return Math.max(0, Math.floor((next - date.getTime()) / 1000));
}

function popcount(n) {
  let c = 0;
  for (n >>>= 0; n; n &= n - 1) c++;
  return c;
}
