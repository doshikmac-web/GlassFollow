<#
 GlassFollow setup - interactive autostart manager.
 Pick apps and flags step by step, or type the whole command line yourself, then install the autostart task.
 Start it with:  GlassFollow-setup.cmd   |   GlassFollow.exe --setup   |   powershell -ExecutionPolicy Bypass -File scripts\setup.ps1
#>
param(
    [string]$ExePath,
    [string]$TaskName = 'GlassFollow'
)

$ErrorActionPreference = 'Stop'
$ConfigDir  = Join-Path $env:APPDATA 'GlassFollow'
$ConfigFile = Join-Path $ConfigDir 'autostart.json'
$ru = (Get-UICulture).Name -like 'ru*'

$Art = @'
  ____  _                   _____         _  _
 / ___|| |  __ _  ___  ___ |  ___|  ___  | || |  ___  __      __
| |  _ | | / _` |/ __|/ __|| |_    / _ \ | || | / _ \ \ \ /\ / /
| |_| || || (_| |\__ \\__ \|  _|  | (_) || || || (_) | \ V  V /
 \____||_| \__,_||___/|___/|_|     \___/ |_||_| \___/   \_/\_/
'@

if ($ru) {
    $T = @{
        exe = 'Программа'; taskOn = 'Автозапуск: установлен'; taskOff = 'Автозапуск: не установлен'
        running = 'Сейчас работает'; stopped = 'Сейчас не запущена'; argsLabel = 'Параметры'
        m1 = '1  Пошаговая настройка (выбрать программы и флаги, установить автозапуск)'
        m2 = '2  Ручная настройка (самому вписать всю командную строку)'
        m3 = '3  Показать установленную задачу автозапуска'
        m4 = '4  Удалить автозапуск'
        m5 = '5  Запустить GlassFollow сейчас'
        m6 = '6  Остановить GlassFollow'
        m7 = '7  Список окон (чтобы подобрать спецификации программ)'
        m0 = '0  Выход'
        choose = 'Выбор'
        appsTitle = 'Программы, для которых включить стекло:'
        appsPrompt = 'Номера через запятую, 0 = ни одной'
        customPrompt = 'Свои спецификации (процесс[:класс[:заголовок]] через пробел), "-" = очистить'
        corner = 'Радиус скругления в пикселях (--corner)'
        thick = 'Толщина края стекла (--thick)'
        fps = 'Лимит кадров (--fps), укажи частоту монитора'
        pad = 'Отступ для выборки в пикселях (--pad)'
        noShot = 'Отключить обработку Win+Shift+S / PrtScn (--no-shot)?'
        extra = 'Любые другие флаги в конец, "-" = очистить'
        delay = 'Задержка после входа в систему, секунд'
        manual = 'Полная командная строка (без пути к exe)'
        preview = 'Будет использована командная строка:'
        confirm = 'Установить автозапуск с этими параметрами?'
        installed = 'Автозапуск установлен. Сработает при следующем входе в систему.'
        restart = 'Перезапустить GlassFollow сейчас с этими параметрами?'
        removed = 'Автозапуск удалён.'
        noTask = 'Задача автозапуска не найдена.'
        noExe = 'GlassFollow.exe не найден. Введи полный путь к нему'
        bad = 'Введи число от {0} до {1}.'
        back = 'Нажми Enter, чтобы продолжить'
        started = 'GlassFollow запущен.'
        stoppedMsg = 'GlassFollow остановлен.'
        keep = 'Enter = оставить значение в скобках'
        style = 'Стиль стекла по умолчанию: correct или uwidgets (--style)'
        alt = 'Программы со ВТОРЫМ стилем (номера через запятую, "-" = нет)'
        uwRefraction = 'Стиль uWidgets: сила преломления 0-100 (--uw-refraction)'
        uwBlur = 'Стиль uWidgets: размытие 0-100 (--uw-blur)'
        uwTheme = 'Стиль uWidgets: тема тонировки, dark или light (--uw-theme)'
    }
} else {
    $T = @{
        exe = 'Program'; taskOn = 'Autostart: installed'; taskOff = 'Autostart: not installed'
        running = 'Running now'; stopped = 'Not running'; argsLabel = 'Arguments'
        m1 = '1  Guided setup (choose apps and flags, install autostart)'
        m2 = '2  Manual setup (type the whole command line yourself)'
        m3 = '3  Show the installed autostart task'
        m4 = '4  Remove autostart'
        m5 = '5  Start GlassFollow now'
        m6 = '6  Stop GlassFollow'
        m7 = '7  List windows (to find app specs)'
        m0 = '0  Exit'
        choose = 'Choose'
        appsTitle = 'Apps to add the glass to:'
        appsPrompt = 'Numbers separated by commas, 0 = none'
        customPrompt = 'Extra specs (process[:class[:title]], space separated), "-" = clear'
        corner = 'Corner radius in pixels (--corner)'
        thick = 'Glass edge thickness (--thick)'
        fps = 'Frame cap (--fps), use your monitor refresh rate'
        pad = 'Sampling margin in pixels (--pad)'
        noShot = 'Disable Win+Shift+S / PrtScn handling (--no-shot)?'
        extra = 'Any other flags to append, "-" = clear'
        delay = 'Delay after logon in seconds'
        manual = 'Full command line (without the exe path)'
        preview = 'Command line that will be used:'
        confirm = 'Install autostart with this command line?'
        installed = 'Autostart installed. It starts after your next logon.'
        restart = 'Restart GlassFollow now with these settings?'
        removed = 'Autostart removed.'
        noTask = 'No autostart task found.'
        noExe = 'GlassFollow.exe not found. Enter its full path'
        bad = 'Enter a number from {0} to {1}.'
        back = 'Press Enter to continue'
        started = 'GlassFollow started.'
        stoppedMsg = 'GlassFollow stopped.'
        keep = 'Enter = keep the value in brackets'
        style = 'Default glass style: correct or uwidgets (--style)'
        alt = 'Apps that use the OTHER style (numbers, comma separated, "-" = none)'
        uwRefraction = 'uWidgets style: refraction strength 0-100 (--uw-refraction)'
        uwBlur = 'uWidgets style: blur 0-100 (--uw-blur)'
        uwTheme = 'uWidgets style: tint theme, dark or light (--uw-theme)'
    }
}

$Presets = @(
    @{ Name = 'Zen Browser';                        Spec = 'zen' },
    @{ Name = 'File Explorer';                      Spec = 'explorer:CabinetWClass' },
    @{ Name = 'Explorer dialogs (delete, ...)';     Spec = '"explorer:#32770"' },
    @{ Name = 'Discord';                            Spec = 'discord' },
    @{ Name = 'Steam (Millennium)';                 Spec = 'steamwebhelper:SDL_app' },
    @{ Name = 'Windows Terminal';                   Spec = 'WindowsTerminal' },
    @{ Name = 'Classic console (cmd / PowerShell)'; Spec = '"*:ConsoleWindowClass"' },
    @{ Name = 'Flow Launcher (experimental)';       Spec = '"Flow.Launcher:HwndWrapper*"' }
)

$cfg = [ordered]@{
    Presets = '1,2,3,4,6'; Custom = ''; Corner = '22'; Thick = '60'; Fps = '200'; Pad = '16'
    NoShot = $false; Extra = ''; Delay = '15'; Manual = ''
    Style = 'correct'; Alt = ''; UwRefraction = '100'; UwBlur = '0'; UwTheme = 'dark'
}
if (Test-Path $ConfigFile) {
    try {
        $saved = Get-Content $ConfigFile -Raw | ConvertFrom-Json
        foreach ($k in @($cfg.Keys)) { if ($null -ne $saved.$k) { $cfg[$k] = $saved.$k } }
    } catch { }
}
function Save-Config {
    New-Item -ItemType Directory -Force -Path $ConfigDir | Out-Null
    $cfg | ConvertTo-Json | Set-Content -Path $ConfigFile -Encoding UTF8
}

# ---------- locate the exe ----------
$Exe = $null
$candidates = @()
if ($ExePath) { $candidates += $ExePath }
$candidates += (Join-Path $PSScriptRoot '..\GlassFollow.exe')
$candidates += (Join-Path $PSScriptRoot '..\src\GlassFollow\bin\Release\net10.0-windows10.0.26100.0\GlassFollow.exe')
foreach ($c in $candidates) { if (Test-Path $c) { $Exe = (Resolve-Path $c).Path; break } }
if (-not $Exe) {
    $p = Read-Host $T.noExe
    if ($p -and (Test-Path $p)) { $Exe = (Resolve-Path $p).Path } else { exit 1 }
}

# ---------- helpers ----------
function Show-Banner {
    Clear-Host
    $colors = 'Cyan', 'Cyan', 'DarkCyan', 'Blue', 'DarkBlue'
    $i = 0
    foreach ($line in ($Art -split "`r?`n")) {
        Write-Host $line -ForegroundColor $colors[[Math]::Min($i, 4)]
        $i++
    }
    Write-Host '  liquid glass for Windows 11' -ForegroundColor DarkGray
    Write-Host ('  ' + ('-' * 60)) -ForegroundColor DarkGray
}

function Get-GlassTask { Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue }
function Test-GlassRunning { return [bool](Get-Process -Name GlassFollow -ErrorAction SilentlyContinue) }

function Show-Status {
    $task = Get-GlassTask
    Write-Host ("  {0}: {1}" -f $T.exe, $Exe)
    if ($task) { Write-Host ('  ' + $T.taskOn) -ForegroundColor Green } else { Write-Host ('  ' + $T.taskOff) -ForegroundColor Yellow }
    if (Test-GlassRunning) { Write-Host ('  ' + $T.running) -ForegroundColor Green } else { Write-Host ('  ' + $T.stopped) -ForegroundColor DarkGray }
    Write-Host ''
}

function Ask([string]$label, $default) {
    $v = Read-Host ("{0} [{1}]" -f $label, $default)
    if ([string]::IsNullOrWhiteSpace($v)) { return "$default" }
    if ($v.Trim() -eq '-') { return '' }
    return $v.Trim()
}

function Ask-Number([string]$label, $default, [double]$min, [double]$max) {
    while ($true) {
        $v = (Ask $label $default).Replace(',', '.')
        $n = 0.0
        if ([double]::TryParse($v, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$n) -and $n -ge $min -and $n -le $max) { return $v }
        Write-Host ('  ' + ($T.bad -f $min, $max)) -ForegroundColor Yellow
    }
}

function Ask-YesNo([string]$label, [bool]$default) {
    $hint = 'y/N'
    if ($default) { $hint = 'Y/n' }
    $v = Read-Host ("{0} ({1})" -f $label, $hint)
    if ([string]::IsNullOrWhiteSpace($v)) { return $default }
    return $v.Trim().ToLower().StartsWith('y') -or $v.Trim().ToLower().StartsWith('д')
}

function Ask-Choice([string]$label, $default, [string[]]$choices) {
    while ($true) {
        $v = (Ask $label $default).ToLower()
        if ($v -eq 'uw') { $v = 'uwidgets' }
        if ($choices -contains $v) { return $v }
        Write-Host ('  ' + ($choices -join ' / ')) -ForegroundColor Yellow
    }
}

function Build-Arguments {
    $parts = @('--hidden', "--corner $($cfg.Corner)", "--thick $($cfg.Thick)", "--fps $($cfg.Fps)", "--pad $($cfg.Pad)", "--style $($cfg.Style)")
    $alt = @("$($cfg.Alt)" -split '[,\s]+' | Where-Object { $_ -match '^\d+$' })
    $other = 'uw'
    if ($cfg.Style -eq 'uwidgets') { $other = 'correct' }
    if ($cfg.Style -eq 'uwidgets' -or $alt.Count -gt 0) {
        $parts += "--uw-refraction $($cfg.UwRefraction)", "--uw-blur $($cfg.UwBlur)", "--uw-theme $($cfg.UwTheme)"
    }
    if ($cfg.NoShot) { $parts += '--no-shot' }
    if ($cfg.Extra) { $parts += $cfg.Extra }
    foreach ($n in ("$($cfg.Presets)" -split '[,\s]+')) {
        if ($n -match '^\d+$') {
            $i = [int]$n - 1
            if ($i -ge 0 -and $i -lt $Presets.Count) {
                $spec = $Presets[$i].Spec
                if ($alt -contains $n) { $spec = '"' + $spec.Trim('"') + '=' + $other + '"' }   # per-app style suffix
                $parts += $spec
            }
        }
    }
    if ($cfg.Custom) { $parts += $cfg.Custom }
    return ($parts -join ' ')
}

function Install-GlassTask([string]$Arguments, [int]$Delay) {
    $action   = New-ScheduledTaskAction -Execute $Exe -Argument $Arguments -WorkingDirectory (Split-Path $Exe)
    $trigger  = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
    $trigger.Delay = "PT${Delay}S"   # let Explorer and Windhawk start first
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 5 -RestartInterval (New-TimeSpan -Minutes 1)
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Force | Out-Null
}

function Start-Glass([string]$Arguments) {
    Stop-Process -Name GlassFollow -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
    Start-Process -FilePath $Exe -ArgumentList $Arguments -WorkingDirectory (Split-Path $Exe) -WindowStyle Hidden
    Write-Host $T.started -ForegroundColor Green
}

function Confirm-And-Install([string]$Arguments) {
    Write-Host ''
    Write-Host $T.preview -ForegroundColor Cyan
    Write-Host "  GlassFollow.exe $Arguments"
    Write-Host ''
    if (-not (Ask-YesNo $T.confirm $true)) { return }
    Install-GlassTask $Arguments ([int]$cfg.Delay)
    Save-Config
    Write-Host $T.installed -ForegroundColor Green
    if (Ask-YesNo $T.restart $true) { Start-Glass $Arguments }
}

function Invoke-Guided {
    Write-Host ''
    Write-Host $T.keep -ForegroundColor DarkGray
    $cfg.Style = Ask-Choice $T.style $cfg.Style @('correct', 'uwidgets')
    Write-Host $T.appsTitle -ForegroundColor Cyan
    $selected = "$($cfg.Presets)" -split '[,\s]+'
    for ($i = 0; $i -lt $Presets.Count; $i++) {
        $mark = ' '
        if ($selected -contains [string]($i + 1)) { $mark = '*' }
        Write-Host ("  [{0}] {1}  {2}" -f $mark, ($i + 1), $Presets[$i].Name)
    }
    $v = Ask $T.appsPrompt $cfg.Presets
    if ($v.Trim() -eq '0') { $v = '' }
    $cfg.Presets = $v
    $cfg.Alt     = Ask $T.alt $cfg.Alt
    $cfg.Custom  = Ask $T.customPrompt $cfg.Custom
    $cfg.Corner  = Ask-Number $T.corner $cfg.Corner 0 500
    $cfg.Thick   = Ask-Number $T.thick  $cfg.Thick  1 2000
    $cfg.Fps     = Ask-Number $T.fps    $cfg.Fps    1 1000
    $cfg.Pad     = Ask-Number $T.pad    $cfg.Pad    0 500
    $usesUw = ($cfg.Style -eq 'uwidgets') -or (@("$($cfg.Alt)" -split '[,\s]+' | Where-Object { $_ -match '^\d+$' }).Count -gt 0)
    if ($usesUw) {
        $cfg.UwRefraction = Ask-Number $T.uwRefraction $cfg.UwRefraction 0 100
        $cfg.UwBlur       = Ask-Number $T.uwBlur       $cfg.UwBlur       0 100
        $cfg.UwTheme      = Ask-Choice $T.uwTheme      $cfg.UwTheme      @('dark', 'light')
    }
    $cfg.NoShot  = Ask-YesNo $T.noShot ([bool]$cfg.NoShot)
    $cfg.Extra   = Ask $T.extra $cfg.Extra
    $cfg.Delay   = Ask-Number $T.delay $cfg.Delay 0 600
    $cfg.Manual  = ''
    Confirm-And-Install (Build-Arguments)
}

function Invoke-Manual {
    Write-Host ''
    Write-Host $T.keep -ForegroundColor DarkGray
    $default = $cfg.Manual
    if (-not $default) { $default = (Build-Arguments) -replace '^--hidden\s*', '' }
    $v = Ask $T.manual $default
    $cfg.Manual = ($v -replace '^--hidden\s*', '')
    $cfg.Delay = Ask-Number $T.delay $cfg.Delay 0 600
    Confirm-And-Install ("--hidden " + $cfg.Manual)
}

function Show-Task {
    $task = Get-GlassTask
    Write-Host ''
    if (-not $task) { Write-Host $T.noTask -ForegroundColor Yellow; return }
    Write-Host ("  State:     {0}" -f $task.State)
    Write-Host ("  Program:   {0}" -f $task.Actions[0].Execute)
    Write-Host ("  {0}: {1}" -f $T.argsLabel, $task.Actions[0].Arguments)
    Write-Host ("  Trigger:   at logon, delay {0}" -f $task.Triggers[0].Delay)
}

# ---------- menu ----------
while ($true) {
    Show-Banner
    Show-Status
    Write-Host ('  ' + $T.m1)
    Write-Host ('  ' + $T.m2)
    Write-Host ('  ' + $T.m3)
    Write-Host ('  ' + $T.m4)
    Write-Host ('  ' + $T.m5)
    Write-Host ('  ' + $T.m6)
    Write-Host ('  ' + $T.m7)
    Write-Host ('  ' + $T.m0)
    Write-Host ''
    $choice = Read-Host $T.choose
    $pause = $true
    switch ($choice) {
        '1' { Invoke-Guided }
        '2' { Invoke-Manual }
        '3' { Show-Task }
        '4' {
            Stop-Process -Name GlassFollow -ErrorAction SilentlyContinue
            if (Get-GlassTask) { Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false; Write-Host $T.removed -ForegroundColor Green }
            else { Write-Host $T.noTask -ForegroundColor Yellow }
        }
        '5' {
            $a = "--hidden " + $cfg.Manual
            if (-not $cfg.Manual) { $a = Build-Arguments }
            Start-Glass $a
        }
        '6' { Stop-Process -Name GlassFollow -ErrorAction SilentlyContinue; Write-Host $T.stoppedMsg -ForegroundColor Green }
        '7' { & $Exe --list --delay 3; Write-Host '' }
        '0' { exit 0 }
        default { $pause = $false }
    }
    if ($pause) { Write-Host ''; [void](Read-Host $T.back) }
}
