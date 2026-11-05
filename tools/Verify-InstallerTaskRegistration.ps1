param([switch]$Worker)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$directory = Join-Path $root '.verification\installer-task-xml'
$receiptPath = Join-Path $directory 'registration-result.json'
$logPath = Join-Path $directory 'registration.log'
$sourceHash = (Get-FileHash (Join-Path $root 'installer.iss') -Algorithm SHA256).Hash
$fixtureHashes = @('plain','unicode') | ForEach-Object { (Get-FileHash (Join-Path $directory "$_.xml") -Algorithm SHA256).Hash }
$key = $sourceHash + ':' + (Get-FileHash $PSCommandPath -Algorithm SHA256).Hash + ':' + ($fixtureHashes -join ':') + ':' + [Environment]::OSVersion.Version.ToString()
if (-not $Worker) {
    if (Test-Path $receiptPath) {
        $receipt = Get-Content $receiptPath -Raw | ConvertFrom-Json
        if ($receipt.success -and $receipt.key -eq $key) {
            Write-Output 'PASS unchanged installer files already registered, replaced, queried and removed through schtasks on this Windows build'
            return
        }
    }
    $isAdmin = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($isAdmin) { & $PSCommandPath -Worker }
    else {
        $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -Worker'
        $process = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -PassThru
        if (-not $process.WaitForExit(60000)) { throw 'Task registration validation is still running; inspect its log before retrying' }
    }
    if (-not (Test-Path $receiptPath)) { throw 'Task registration validation did not produce a result' }
    $receipt = Get-Content $receiptPath -Raw | ConvertFrom-Json
    if (-not $receipt.success -or $receipt.key -ne $key) { throw "Task registration validation failed: $($receipt.error). See $logPath" }
    Write-Output 'PASS actual schtasks registration, replacement, query and cleanup for plain and Unicode paths; v0.1.402 encoding failure reproduced'
    return
}
$taskName = 'RELYR-Installer-Validation-' + [Guid]::NewGuid().ToString('N')
$registered = $false
$success = $false
$failure = ''
Start-Transcript -LiteralPath $logPath -Force | Out-Null
try {
    $scheduler = New-Object -ComObject 'Schedule.Service'
    $scheduler.Connect()
    $folder = $scheduler.GetFolder('\')
    foreach ($case in @('plain','unicode')) {
        $file = Join-Path $directory "$case.xml"
        # Both imports use the exact file emitted by Inno Setup, unchanged.
        foreach ($operation in @('register','replace')) {
            & "$env:SystemRoot\System32\schtasks.exe" /Create /TN $taskName /XML $file /F
            if ($LASTEXITCODE -ne 0) { throw "$case $operation failed ($LASTEXITCODE)" }
            $registered = $true
            $expected = [xml]::new()
            $expected.Load($file)
            $actual = [xml]$folder.GetTask($taskName).Xml
            if ($actual.Task.Principals.Principal.UserId -ne $expected.Task.Principals.Principal.UserId -or
                $actual.Task.Principals.Principal.RunLevel -ne 'HighestAvailable' -or
                $actual.Task.Actions.Exec.Command -ne $expected.Task.Actions.Exec.Command -or
                $actual.Task.Actions.Exec.WorkingDirectory -ne $expected.Task.Actions.Exec.WorkingDirectory -or
                $actual.Task.Actions.Exec.Arguments -ne $expected.Task.Actions.Exec.Arguments) { throw "$case task round-trip mismatch" }
            Write-Output "PASS $case ${operation}: exact file, account, elevation, path and arguments"
        }
        & "$env:SystemRoot\System32\schtasks.exe" /Delete /TN $taskName /F
        if ($LASTEXITCODE -ne 0) { throw 'Validation task cleanup failed' }
        $registered = $false
    }
    # Negative control: the previous release's UTF-8 file/declaration must fail
    # through the same CLI. Successful positive controls rule out permission failures.
    $previousFile = Join-Path $directory 'v0.1.402-regression.xml'
    $previousXml = [IO.File]::ReadAllText((Join-Path $directory 'plain.xml')).Replace('encoding="UTF-16"','encoding="UTF-8"')
    [IO.File]::WriteAllText($previousFile, $previousXml, [Text.UTF8Encoding]::new($true))
    $ErrorActionPreference = 'Continue'
    & "$env:SystemRoot\System32\schtasks.exe" /Create /TN $taskName /XML $previousFile /F 2>&1 | ForEach-Object { Write-Output $_.ToString() }
    $previousResult = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($previousResult -eq 0) { $registered = $true; throw 'Previous encoding regression unexpectedly succeeded' }
    Write-Output 'PASS previous release encoding rejected by schtasks'
    $success = $true
} catch { $failure = $_.Exception.Message; Write-Output "FAIL $failure" }
finally {
    if ($registered) {
        & "$env:SystemRoot\System32\schtasks.exe" /Delete /TN $taskName /F
        if ($LASTEXITCODE -ne 0) { $success = $false; $failure += '; validation task cleanup failed' }
    }
    @{success=$success; error=$failure; key=$key; task=$taskName; completed=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding UTF8
    Stop-Transcript | Out-Null
}
if (-not $success) { exit 1 }
