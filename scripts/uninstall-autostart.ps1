param([string]$TaskName = 'GlassFollow')
Stop-Process -Name GlassFollow -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
Write-Host "Task '$TaskName' removed."
