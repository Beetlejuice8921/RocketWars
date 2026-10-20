# Выкладка сервера рейтинга на Cloudflare (Worker rocketwars-api + база D1 rocketwars). Нужен Node.js.
#   powershell -ExecutionPolicy Bypass -File Backend\deploy.ps1
# В первый раз: создаёт базу и прописывает её id в wrangler.toml, применяет schema.sql, создаёт секрет TOKEN_SECRET
# и спрашивает токен бота (@BotFather → /mybots → API Token) — им сервер проверяет подпись Telegram.
# Повторные запуски только применяют schema.sql (безопасно) и выкладывают код.
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
if ($LASTEXITCODE) { throw 'schema.sql не применилась' }

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

Write-Host "`nГотово. Проверка: https://rocketwars-api.<поддомен>.workers.dev/v1/health"
