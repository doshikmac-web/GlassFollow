<#
 Registers a Task Scheduler task that starts GlassFollow at logon (no console window).
 Run from the release folder:  .\scripts\install-autostart.ps1
 Custom apps:  .\scripts\install-autostart.ps1 -Arguments 'zen discord "explorer:#32770"'
#>
param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\GlassFollow.exe'),
    [string]$Arguments = '--thick 60 zen explorer:CabinetWClass "explorer:#32770" discord WindowsTerminal',
    [int]$DelaySeconds = 15,
    [string]$TaskName = 'GlassFollow'
)

$exe = (Resolve-Path $ExePath).Path
if ($Arguments -notmatch '--hidden') { $Arguments = "--hidden $Arguments" }

$action   = New-ScheduledTaskAction -Execute $exe -Argument $Arguments -WorkingDirectory (Split-Path $exe)
$trigger  = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$trigger.Delay = "PT${DelaySeconds}S"   # give Explorer and Windhawk time to start first
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Force | Out-Null
Write-Host "Task '$TaskName' registered. It will start at your next logon."
Write-Host "Start it now:  Start-ScheduledTask -TaskName $TaskName"
Write-Host "Stop it:       Stop-Process -Name GlassFollow"
