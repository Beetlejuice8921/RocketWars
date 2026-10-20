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
