# Выкладка веб-сборки VK на хостинг VK Mini Apps (бесплатный CDN).
# Берёт самую новую Build/<версия>/VK (папка Build — рядом с папкой проекта, её создаёт RocketWars → Build),
# копирует в Deploy/VK/build и выкладывает через @vkontakte/vk-miniapps-deploy. Нужен Node.js (npx).
#
#   powershell -ExecutionPolicy Bypass -File Deploy/VK/deploy.ps1 [-Version 1.0.3]
#
# Один раз: вписать app_id игры в vk-hosting-config.json (dev.vk.com → Приложения → игра → ID).
# Доступ: при первом запуске утилита попросит подтвердить вход в VK и покажет токен. Чтобы не входить каждый раз,
# сохранить его в переменную окружения MINI_APPS_ACCESS_TOKEN (не в файлы репозитория).
# Адреса в «Размещении» (iframe) утилита обновит сама. Лимиты VK: 24 выкладки в сутки, архив до 300 МБ.

param([string]$Version)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$buildRoot = Join-Path $here '..\..\..\Build' | Resolve-Path -ErrorAction SilentlyContinue
if (-not $buildRoot) { throw "Нет папки Build рядом с проектом — сначала собрать: RocketWars → Build → VK (Web)" }

$config = Get-Content (Join-Path $here 'vk-hosting-config.json') -Raw | ConvertFrom-Json
if (-not $config.app_id) { throw "Не задан app_id в Deploy/VK/vk-hosting-config.json" }

if (-not $Version) {
    # самая новая версия, у которой есть сборка VK (1.0.10 новее 1.0.9)
    $Version = Get-ChildItem $buildRoot -Directory |
        Where-Object { (Test-Path (Join-Path $_.FullName 'VK\index.html')) -and ($_.Name -as [version]) } |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1 -ExpandProperty Name
    if (-not $Version) { throw "В $buildRoot нет ни одной сборки VK" }
}
$source = Join-Path $buildRoot "$Version\VK"
if (-not (Test-Path (Join-Path $source 'index.html'))) { throw "Нет сборки $source" }

$target = Join-Path $here 'build'
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
Copy-Item $source $target -Recurse
$sizeMb = (Get-ChildItem $target -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Rocket Wars {0} → VK app {1} ({2:0.0} МБ)" -f $Version, $config.app_id, $sizeMb)

Push-Location $here
try {
    npx --yes @vkontakte/vk-miniapps-deploy
    if ($LASTEXITCODE -ne 0) { throw "vk-miniapps-deploy завершился с кодом $LASTEXITCODE" }
}
finally {
    Pop-Location
}
