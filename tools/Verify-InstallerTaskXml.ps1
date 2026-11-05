$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root '.verification\installer-task-xml'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$source = Get-Content (Join-Path $root 'installer.iss') -Raw -Encoding UTF8
$start = $source.IndexOf('function EscapeTaskXml(')
$end = $source.IndexOf('function LauncherSetupError(')
if ($start -lt 0 -or $end -le $start) { throw 'Installer XML functions not found' }
$functions = $source.Substring($start, $end - $start)
$probe = @'
[Setup]
AppName=RELYR XML validation
AppVersion=1
DefaultDirName={tmp}\RELYR-XML-validation
CreateAppDir=no
Uninstallable=no
PrivilegesRequired=lowest
OutputDir=.
OutputBaseFilename=task-xml-probe
[Code]
'@ + "`r`n" + $functions + @'

function InitializeSetup(): Boolean;
var
  Sid: String;
begin
  Result := False;
  Sid := CurrentTaskUserSid();
  if Sid = '' then RaiseException('No current account SID');
  if not SaveLauncherTaskXml(ExpandConstant('{src}\unicode.xml'), Sid,
    'C:\Apps\José & 日本語\RELYR') then RaiseException('Unicode write failed');
  if not SaveLauncherTaskXml(ExpandConstant('{src}\plain.xml'), Sid,
    'C:\Program Files\RELYR') then RaiseException('Plain write failed');
  if not SaveLauncherTaskXml(ExpandConstant('{src}\escaping.xml'),
    'DOMAIN\José & <test> "quoted" ''apostrophe''', 'C:\Apps\RELYR') then RaiseException('Escaping write failed');
  { Reproduce the previous unescaped, ANSI-encoded document, using its real API. }
  if not SaveStringToFile(ExpandConstant('{src}\legacy.xml'),
    '<?xml version="1.0"?><Task><UserId>José & 日本語</UserId></Task>', False) then RaiseException('Legacy write failed');
end;
'@
$probeFile = Join-Path $output 'task-xml-probe.iss'
[IO.File]::WriteAllText($probeFile, $probe, [Text.UTF8Encoding]::new($true))
& 'C:\Users\freecar\AppData\Local\Programs\Inno Setup 6\ISCC.exe' $probeFile | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Installer XML probe compilation failed' }
# InitializeSetup writes fixtures and returns False before any installation.
$process = Start-Process (Join-Path $output 'task-xml-probe.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Installer XML probe timed out' }
$currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$service = New-Object -ComObject 'Schedule.Service'
$service.Connect()
$folder = $service.GetFolder('\')
foreach ($case in @('plain','unicode')) {
    $file = Join-Path $output "$case.xml"
    $bytes = [IO.File]::ReadAllBytes($file)
    if ($bytes.Length -lt 4 -or $bytes[0] -ne 0xFF -or $bytes[1] -ne 0xFE) { throw 'Expected UTF-16LE BOM' }
    $document = [xml]::new()
    $document.Load($file)
    if ($document.Task.Principals.Principal.UserId -ne $currentSid) { throw 'Account SID mismatch' }
    $expectedDirectory = if ($case -eq 'plain') { 'C:\Program Files\RELYR' } else { 'C:\Apps\José & 日本語\RELYR' }
    if ($document.Task.Actions.Exec.Command -ne "$expectedDirectory\RELYR.exe" -or $document.Task.Actions.Exec.WorkingDirectory -ne $expectedDirectory) { throw 'Task path did not round-trip' }
    if ($document.Task.Actions.Exec.Arguments -ne '--elevated-task "$(Arg0)"' -or $document.Task.Principals.Principal.LogonType -ne 'InteractiveToken' -or $document.Task.Principals.Principal.RunLevel -ne 'HighestAvailable') { throw 'Launcher contract changed' }
    # TASK_VALIDATE_ONLY: Windows validates the actual task XML, without creating
    # a task, replacing the user's launcher, or executing any application.
    # Validate the exact decoded file, with no declaration changes or XML
    # reserialization. The v0.1.402 check masked the installer encoding bug.
    $taskBstr = [IO.File]::ReadAllText($file)
    $null = $folder.RegisterTask('RELYR XML validation', $taskBstr, 1, $null, $null, 3, $null)
    Write-Output "PASS Inno-generated $case task: UTF-16LE, SID, paths, launch contract, unmodified Windows validation"
}
$escaped = [xml]::new()
$escaped.Load((Join-Path $output 'escaping.xml'))
if ($escaped.Task.Principals.Principal.UserId -ne 'DOMAIN\José & <test> "quoted" ''apostrophe''') { throw 'XML escaping changed the account value' }
$legacyRejected = $false
try { $legacy = [xml]::new(); $legacy.Load((Join-Path $output 'legacy.xml')) } catch { $legacyRejected = $true }
if (-not $legacyRejected) { throw 'Legacy regression fixture unexpectedly parsed' }
Write-Output 'PASS XML escaping round-trip and legacy malformed XML reproduction'
# Exercise schtasks' real file import, including registration and replacement.
# This requires elevation, just as the real installer does. The worker only
# touches uniquely named validation tasks, never the installed RELYR task.
& (Join-Path $PSScriptRoot 'Verify-InstallerTaskRegistration.ps1')
$global:LASTEXITCODE = 0
