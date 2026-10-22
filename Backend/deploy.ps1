# Выкладка сервера рейтинга на Cloudflare (Worker rocketwars-api + база D1 rocketwars). Нужен Node.js.
#   powershell -ExecutionPolicy Bypass -File Backend\deploy.ps1
# В первый раз: создаёт базу и прописывает её id в wrangler.toml, применяет schema.sql, создаёт секрет TOKEN_SECRET
# и спрашивает токен бота (@BotFather → /mybots → API Token) — им сервер проверяет подпись Telegram.
# Повторные запуски только применяют schema.sql (безопасно) и выкладывают код.
# Бот: в первый раз создаёт секрет вебхука, направляет вебхук @RocketWarsGameBot на этот Worker, ставит меню команд
# и спрашивает ваш Telegram ID (админ: стримеры-партнёры, рассылка). -SetupBot — настроить бота заново (новый секрет).
param([switch]$SetupBot, [string]$ApiUrl = 'https://rocketwars-api.tg-placeholder.workers.dev')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$wrangler = @('--yes', 'wrangler@latest')
$toml = Join-Path $PSScriptRoot 'wrangler.toml'

# 1. база D1
$config = Get-Content $toml -Raw -Encoding UTF8
if ($config -match 'database_id = "0{8}-') {
    # вывод wrangler не перехватываем: с перехваченным выводом он считает, что его запустил не человек, и просит API-токен
    $existing = (npx @wrangler d1 list --json | Out-String | ConvertFrom-Json) | Where-Object { $_.name -eq 'rocketwars' }
    if (-not $existing) {
        Write-Host 'Создаём базу D1 rocketwars…'
        npx @wrangler d1 create rocketwars
        if ($LASTEXITCODE) { throw 'Не удалось создать базу' }
        $existing = (npx @wrangler d1 list --json | Out-String | ConvertFrom-Json) | Where-Object { $_.name -eq 'rocketwars' }
    }
    if (-not $existing.uuid) { throw 'База rocketwars не найдена' }
    $config = $config -replace 'database_id = "[^"]*"', "database_id = `"$($existing.uuid)`""
    [IO.File]::WriteAllText($toml, $config, (New-Object Text.UTF8Encoding $false))
    Write-Host "База: $($existing.uuid) (id записан в wrangler.toml)"
}
npx @wrangler d1 execute rocketwars --remote --file schema.sql --yes
if ($LASTEXITCODE) {
    # --file идёт через API импорта, который иногда отвечает «Authentication error [code: 10000]» при живом входе;
    # запасной путь — те же команды обычным запросом (--command, другой API). Схема безопасна для повтора.
    Write-Warning 'Импорт schema.sql не прошёл — применяем её запросом'
    $sql = ((Get-Content schema.sql -Encoding UTF8) | ForEach-Object { ($_ -replace '--.*$', '').TrimEnd() } | Where-Object { $_ }) -join ' '
    npx @wrangler d1 execute rocketwars --remote --command $sql --yes
    if ($LASTEXITCODE) { throw 'schema.sql не применилась. Попробуйте перелогиниться: npx.cmd wrangler logout, затем npx.cmd wrangler login' }
}

# 2. код
npx @wrangler deploy
if ($LASTEXITCODE) { throw 'Выкладка не удалась' }

# 3. секреты (после первой выкладки: секрет нельзя добавить к ещё не созданному Worker)
$secrets = (npx @wrangler secret list --format json 2>$null | Out-String)
if ($secrets -notmatch '"TOKEN_SECRET"') {
    $bytes = New-Object byte[] 32
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    ([BitConverter]::ToString($bytes) -replace '-', '').ToLower() | npx @wrangler secret put TOKEN_SECRET
}
if ($secrets -notmatch '"BOT_TOKEN"') {
    $token = Read-Host 'Токен бота @RocketWarsGameBot (BotFather → /mybots → API Token)'
    if ($token) { $token.Trim() | npx @wrangler secret put BOT_TOKEN }
    else { Write-Warning 'BOT_TOKEN не задан: вход через Telegram не заработает, пока не выполните npx wrangler secret put BOT_TOKEN' }
}

# 4. бот: вебхук + команды. Секрет нельзя прочитать обратно, поэтому настройка идёт вместе с созданием нового секрета
if ($secrets -notmatch '"WEBHOOK_SECRET"' -or $SetupBot) {
    $bytes = New-Object byte[] 24
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $hook = ([BitConverter]::ToString($bytes) -replace '-', '').ToLower()
    $hook | npx @wrangler secret put WEBHOOK_SECRET
    Start-Sleep 5 # новый секрет доезжает до Worker не мгновенно
    $setup = Invoke-RestMethod -Method Post "$ApiUrl/tg/setup" -Headers @{ 'X-Setup-Key' = $hook }
    Write-Host "Бот: вебхук $($setup.webhook.ok), команды $($setup.commands)"
}
if ($secrets -notmatch '"ADMIN_IDS"') {
    $admin = Read-Host 'Ваш Telegram ID — админ бота (напишите @RocketWarsGameBot команду /id). Пусто — пропустить'
    if ($admin) { $admin.Trim() | npx @wrangler secret put ADMIN_IDS }
}

Write-Host "`nГотово. Проверка: $ApiUrl/v1/health"
