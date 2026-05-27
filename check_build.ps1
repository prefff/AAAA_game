Write-Host "=== build.log info ==="
if (-not (Test-Path build.log)) { Write-Host "build.log нет"; exit }
$info = Get-Item build.log
Write-Host ("Size: " + [math]::Round($info.Length/1KB, 1) + " KB; LastWrite: " + $info.LastWriteTime)

Write-Host ""
Write-Host "=== Ищу строки с ошибками / итогом сборки ==="
Select-String -Path build.log -Pattern 'error CS|error:|FAILED|Build failure|BuildFailedException|cannot|Internal compiler error|Aborting batchmode|Display\sProgress\s*[Cc]anceled|Build completed|Build succeeded|BuildReport|BuildResult|ExitCode: (?!0\b)' -CaseSensitive:$false |
    Select-Object -Last 50 |
    ForEach-Object { Write-Host ("[" + $_.LineNumber + "] " + $_.Line) }

Write-Host ""
Write-Host "=== Последние 80 строк build.log ==="
Get-Content build.log -Tail 80