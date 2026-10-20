# Выкладка веб-сборки Telegram на Cloudflare Workers (Worker rocketwars-tg, только статика — см. wrangler.toml).
# Берёт самую новую Build/<версия>/Telegram (папка Build — рядом с папкой проекта, её создаёт RocketWars → Build),
# копирует в Deploy/Telegram/build и выкладывает через wrangler. Нужен Node.js (npx).
#
#   powershell -ExecutionPolicy Bypass -File Deploy/Telegram/deploy.ps1 [-Version 1.0.3]
#
# Доступ: один раз `npx wrangler login` (откроет браузер, подтвердить вход в Cloudflare).
# Адрес игры не меняется между выкладками — в @BotFather ничего обновлять не нужно.

param([string]$Version)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$buildRoot = Join-Path $here '..\..\..\Build' | Resolve-Path -ErrorAction SilentlyContinue
if (-not $buildRoot) { throw "Нет папки Build рядом с проектом — сначала собрать: RocketWars → Build → Telegram Mini App (Web)" }

if (-not $Version) {
    # самая новая версия, у которой есть сборка Telegram (1.0.10 новее 1.0.9)
    $Version = Get-ChildItem $buildRoot -Directory |
        Where-Object { (Test-Path (Join-Path $_.FullName 'Telegram\index.html')) -and ($_.Name -as [version]) } |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1 -ExpandProperty Name
    if (-not $Version) { throw "В $buildRoot нет ни одной сборки Telegram" }
}
$source = Join-Path $buildRoot "$Version\Telegram"
if (-not (Test-Path (Join-Path $source 'index.html'))) { throw "Нет сборки $source" }

$target = Join-Path $here 'build'
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
Copy-Item $source $target -Recurse
$sizeMb = (Get-ChildItem $target -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Rocket Wars {0} → rocketwars-tg ({1:0.0} МБ)" -f $Version, $sizeMb)

Push-Location $here
try {
    npx --yes wrangler deploy --message "Rocket Wars $Version"
    if ($LASTEXITCODE -ne 0) { throw "wrangler deploy завершился с кодом $LASTEXITCODE" }
}
finally {
    Pop-Location
}
