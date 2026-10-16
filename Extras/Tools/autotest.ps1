# Автотест Rocket Wars: хост + клиенты (в т.ч. через «плохую сеть»), сценарии выхода и позднего входа.
# Запуск: powershell -File Tools\autotest.ps1 [-Exe путь] [-Out папка_логов]
param(
    [string]$Exe = "D:\ClaudeRocketWars\Build\RocketWars5\Rocket Wars.exe",
    [string]$Out = "$PSScriptRoot\autotest_logs",
    [int]$Delay = 75, [int]$Jitter = 20, [double]$Loss = 2
)

New-Item -ItemType Directory -Force $Out | Out-Null
Get-ChildItem $Out -Filter *.log | Remove-Item -Force

$headless = @("-batchmode", "-nographics")
function Start-Game([string]$name, [string[]]$extra) {
    $args = $headless + $extra + @("-autotest", "-name", $name, "-logFile", "$Out\$name.log")
    Start-Process -FilePath $Exe -ArgumentList $args -PassThru -WindowStyle Hidden
}

$proxy = Start-Process -FilePath python -ArgumentList @("$PSScriptRoot\netsim.py", "--listen", "7790", "--target", "127.0.0.1:7777",
    "--delay", $Delay, "--jitter", $Jitter, "--loss", $Loss) -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput "$Out\netsim.txt" -RedirectStandardError "$Out\netsim_err.txt"

$hostProc = Start-Game "Host" @("-host", "-bots", "-expect", "5")
Start-Sleep 4
$direct = Start-Game "Direct" @("-join", "127.0.0.1")
$laggy  = Start-Game "Laggy"  @("-join", "127.0.0.1", "-port", "7790")
$leaver = Start-Game "Leaver" @("-join", "127.0.0.1")
$laggy2 = Start-Game "Laggy2" @("-join", "127.0.0.1", "-port", "7790")
$direct2 = Start-Game "Direct2" @("-join", "127.0.0.1")

Start-Sleep 22
Stop-Process -Id $leaver.Id -Force -ErrorAction SilentlyContinue
"[orchestrator] killed Leaver at +26s" | Out-File "$Out\orchestrator.txt"
Start-Sleep 8
$late = Start-Game "Late" @("-join", "127.0.0.1", "-port", "7790")

# ждём, пока хост доиграет матч и выйдет сам
$hostProc.WaitForExit(240000) | Out-Null
Start-Sleep 8
foreach ($p in @($direct, $laggy, $late, $laggy2, $direct2, $hostProc)) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
Stop-Process -Id $proxy.Id -Force -ErrorAction SilentlyContinue
"[orchestrator] done" | Out-File "$Out\orchestrator.txt" -Append
