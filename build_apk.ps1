param(
    [int]$WaitForUnityCloseSec = 60
)

$ErrorActionPreference = "Stop"

# 1) Дождаться, пока пользователь закроет Unity Editor
$deadline = (Get-Date).AddSeconds($WaitForUnityCloseSec)
while ($true) {
    $proc = Get-Process Unity -ErrorAction SilentlyContinue
    if (-not $proc) {
        Write-Host "Unity Editor закрыт. Запускаю сборку..."
        break
    }
    if ((Get-Date) -gt $deadline) {
        Write-Warning "Unity всё ещё запущен после $WaitForUnityCloseSec сек ожидания. Прерываю."
        exit 2
    }
    Write-Host "Unity ещё запущен (PID $($proc.Id -join ', ')), жду..."
    Start-Sleep -Seconds 3
}

# 2) Очистить старый лог
$logPath = Join-Path $PSScriptRoot "build.log"
if (Test-Path $logPath) { Remove-Item $logPath -Force }

# 3) Запустить Unity build
$unity = "C:\Program Files\Unity\Hub\Editor\6000.2.8f1\Editor\Unity.exe"
$args = @(
    "-quit",
    "-batchmode",
    "-nographics",
    "-projectPath", $PSScriptRoot,
    "-buildTarget", "Android",
    "-executeMethod", "Game.EditorTools.BuildScript.BuildAndroid",
    "-logFile", $logPath
)

Write-Host "Команда: $unity $($args -join ' ')"
$proc = Start-Process -FilePath $unity -ArgumentList $args -PassThru -NoNewWindow
Write-Host "Unity запущен, PID=$($proc.Id). Лог: $logPath"
Write-Host "Ожидание завершения..."
$proc.WaitForExit()
$exit = $proc.ExitCode
Write-Host "Unity завершился с кодом: $exit"

# 4) Показать tail лога
if (Test-Path $logPath) {
    Write-Host "--- build.log (последние 80 строк) ---"
    Get-Content $logPath -Tail 80
}

# 5) Проверить, что APK создан
$apkPath = Join-Path $PSScriptRoot "Builds\Android\AAAA_game.apk"
if (Test-Path $apkPath) {
    $size = (Get-Item $apkPath).Length / 1MB
    Write-Host ("APK создан: " + $apkPath + " (" + [math]::Round($size, 1) + " MB)")
    exit 0
} else {
    Write-Warning "APK не найден по пути: $apkPath"
    exit 1
}