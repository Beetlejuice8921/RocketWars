-- Rocket Wars: рейтинг (Cloudflare D1). Применить: npx wrangler d1 execute rocketwars --remote --file schema.sql
-- Повторный запуск безопасен (IF NOT EXISTS).

-- аккаунт игрока: Telegram (проверенный подписью initData) или гость (случайный ключ устройства)
CREATE TABLE IF NOT EXISTS players (
  id         INTEGER PRIMARY KEY AUTOINCREMENT,
  provider   TEXT    NOT NULL,            -- telegram | guest
  ext_id     TEXT    NOT NULL,            -- id в Telegram; у гостя — sha256 ключа
  name       TEXT    NOT NULL DEFAULT '',
  verified   INTEGER NOT NULL DEFAULT 0,  -- 1 — аккаунт площадки: попадает в общий рейтинг и подтверждает матчи
  created_at INTEGER NOT NULL,
  UNIQUE (provider, ext_id)
);

-- в какие дни игрок заходил в игру (вход — при запуске): для /stats бота
CREATE TABLE IF NOT EXISTS player_days (
  day       TEXT    NOT NULL,             -- 'YYYY-MM-DD' по Москве
  player_id INTEGER NOT NULL,
  PRIMARY KEY (day, player_id)
);

-- матч, который хост зарегистрировал при старте; итог принимается, когда отчёты игроков сходятся
CREATE TABLE IF NOT EXISTS matches (
  id          TEXT    PRIMARY KEY,
  host_id     INTEGER NOT NULL,
  platform    TEXT    NOT NULL DEFAULT '',
  created_at  INTEGER NOT NULL,
  final_hash  TEXT,                       -- принятая таблица итогов (sha256); NULL — ещё ждём отчёты
  finalized_at INTEGER
);
CREATE INDEX IF NOT EXISTS matches_host ON matches (host_id, created_at);

-- отчёт каждого игрока: вся таблица итогов (её хэш) + какая строка — его
CREATE TABLE IF NOT EXISTS match_reports (
  match_id   TEXT    NOT NULL,
  player_id  INTEGER NOT NULL,
  row_id     INTEGER NOT NULL,            -- NetworkObjectId игрока в этом матче
  hash       TEXT    NOT NULL,
  verified   INTEGER NOT NULL,
  report     TEXT    NOT NULL,            -- JSON для разборов
  created_at INTEGER NOT NULL,
  PRIMARY KEY (match_id, player_id),
  UNIQUE (match_id, row_id)
);

-- начисленные очки: одна строка на игрока и матч (защита от двойного начисления)
CREATE TABLE IF NOT EXISTS match_credits (
  match_id    TEXT    NOT NULL,
  player_id   INTEGER NOT NULL,
  season      TEXT    NOT NULL,
  points      INTEGER NOT NULL,
  practice    INTEGER NOT NULL,           -- 1 — тренировка (один живой игрок + боты)
  rank_before INTEGER,
  rank_after  INTEGER,
  created_at  INTEGER NOT NULL,
  PRIMARY KEY (match_id, player_id)
);

-- очки за сезон: season = '2026-W43' (неделя ISO, сброс в понедельник 00:00 UTC) или 'all'
CREATE TABLE IF NOT EXISTS scores (
  player_id       INTEGER NOT NULL,
  season          TEXT    NOT NULL,
  points          INTEGER NOT NULL DEFAULT 0,
  matches         INTEGER NOT NULL DEFAULT 0,
  wins            INTEGER NOT NULL DEFAULT 0,
  kills           INTEGER NOT NULL DEFAULT 0,
  practice_points INTEGER NOT NULL DEFAULT 0,
  updated_at      INTEGER NOT NULL,
  PRIMARY KEY (player_id, season)
);
CREATE INDEX IF NOT EXISTS scores_rank ON scores (season, points DESC);

-- ---------- бот и стримы ----------

-- кому бот может писать: нажал /start в боте или разрешил боту писать из Mini App (allows_write_to_pm)
CREATE TABLE IF NOT EXISTS subscribers (
  tg_id      TEXT    PRIMARY KEY,
  name       TEXT    NOT NULL DEFAULT '',
  lang       TEXT    NOT NULL DEFAULT 'ru', -- ru | en (язык сообщений бота)
  active     INTEGER NOT NULL DEFAULT 1,    -- 0 — /stop или бот заблокирован
  source     TEXT    NOT NULL DEFAULT '',   -- bot | webapp
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS subscribers_active ON subscribers (active, lang);

-- стримеры-партнёры: только они делают анонсы на всех подписчиков (добавляет админ командой бота)
CREATE TABLE IF NOT EXISTS streamers (
  tg_id       TEXT    PRIMARY KEY,
  name        TEXT    NOT NULL,
  channel_url TEXT    NOT NULL DEFAULT '',
  added_at    INTEGER NOT NULL
);

-- стрим: стример открыл стрим-комнату; итоги рассылаются, когда он её закрыл (или пропал)
CREATE TABLE IF NOT EXISTS streams (
  id            TEXT    PRIMARY KEY,
  streamer_tg   TEXT    NOT NULL,
  room_code     TEXT    NOT NULL,
  started_at    INTEGER NOT NULL,
  last_seen     INTEGER NOT NULL,          -- игра стримера отмечается раз в несколько минут
  announces     INTEGER NOT NULL DEFAULT 0,
  last_announce INTEGER NOT NULL DEFAULT 0,
  ended_at      INTEGER,
  results_sent  INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS streams_room ON streams (room_code, ended_at);
CREATE INDEX IF NOT EXISTS streams_streamer ON streams (streamer_tg, ended_at);

-- кто играл на стриме (только аккаунты Telegram): для итогов и наградного скина
CREATE TABLE IF NOT EXISTS stream_players (
  stream_id  TEXT    NOT NULL,
  player_id  INTEGER NOT NULL,
  tg_id      TEXT    NOT NULL,
  matches    INTEGER NOT NULL DEFAULT 0,
  wins       INTEGER NOT NULL DEFAULT 0,
  best_place INTEGER NOT NULL DEFAULT 99,
  updated_at INTEGER NOT NULL,
  PRIMARY KEY (stream_id, player_id)
);

-- исходящие сообщения бота: рассылка ставится сюда, cron отправляет пачками (лимиты Telegram и Cloudflare)
CREATE TABLE IF NOT EXISTS outbox (
  id         INTEGER PRIMARY KEY AUTOINCREMENT,
  chat_id    TEXT    NOT NULL,
  text       TEXT    NOT NULL,
  markup     TEXT,
  kind       TEXT    NOT NULL DEFAULT '',
  attempts   INTEGER NOT NULL DEFAULT 0,
  created_at INTEGER NOT NULL
);

-- скины-награды (номера Core/Skins), выданные аккаунту
CREATE TABLE IF NOT EXISTS player_skins (
  player_id  INTEGER NOT NULL,
  skin       INTEGER NOT NULL,
  source     TEXT    NOT NULL DEFAULT '',
  created_at INTEGER NOT NULL,
  PRIMARY KEY (player_id, skin)
);
