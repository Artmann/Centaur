#Requires -Version 7.0
<#
.SYNOPSIS
    Drives the Centaur terminal emulator the way a user does: real keystrokes, real mouse,
    real screenshots of the real window. Used by the verify-centaur skill.

.DESCRIPTION
    Centaur is a Windows desktop GUI app with no scripting port, so this harness talks to it
    through Win32 - SendInput for keyboard and mouse, PrintWindow for capture. Each run gets
    a directory under .verify/ holding its state, its isolated config and its artifacts.

    Isolation: the instance is launched with CENTAUR_CONFIG_DIR pointed at the run's own
    config directory, so it starts from a blank session instead of restoring the user's tabs,
    and its history and settings never touch %APPDATA%\Centaur. That makes it safe to drive a
    verification instance while the user has their own Centaur open. This harness only ever
    signals the PID it started.

.EXAMPLE
    $c = '.claude/skills/verify-centaur/control-centaur.ps1'
    & $c build
    & $c launch
    & $c doctor
    & $c type -Text 'Get-Date'
    & $c key -Combo enter
    & $c shot -Name after-command
    & $c cleanup
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('build', 'launch', 'doctor', 'focus', 'type', 'key', 'click', 'drag',
        'shot', 'pixel', 'windows', 'state', 'workdir', 'artifacts', 'stop', 'cleanup')]
    [string]$Command,

    # type
    [string]$Text,

    # key: 'enter', 'ctrl+t', 'ctrl+shift+p', 'shift+pageup', 'ctrl+comma', 'escape', ...
    [string]$Combo,

    # click / drag: client coordinates, relative to the window's top-left client pixel
    [int]$X,
    [int]$Y,
    [int]$ToX,
    [int]$ToY,
    [ValidateSet('left', 'right')]
    [string]$Button = 'left',

    # click: how many clicks in a row (2 = double-click). key: how many times to press the combo.
    [int]$Count = 1,

    # shot / pixel
    [string]$Name,

    # shot: photograph the screen where the window sits instead of asking the window to draw
    # itself. Slower and occlusion-sensitive, but it is the only way to see a popup menu.
    [switch]$Screen,

    # launch
    [int]$Width = 1200,
    [int]$Height = 800,
    [int]$Left = 80,
    [int]$Top = 80,

    # any command: target a specific run instead of the most recent one
    [string]$Run,

    # milliseconds to settle after an input before returning
    [int]$Settle = 250
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$verifyRoot = Join-Path $repoRoot '.verify'
$exePath = Join-Path $repoRoot 'src\Centaur.App\bin\Debug\net9.0-windows\Centaur.App.exe'
$userConfigDir = Join-Path $env:APPDATA 'Centaur'

# ---------------------------------------------------------------- Win32 interop

Add-Type -AssemblyName System.Drawing

if (-not ('Centaur.Win32' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Centaur {
  [StructLayout(LayoutKind.Sequential)]
  public struct RECT { public int Left, Top, Right, Bottom; }

  [StructLayout(LayoutKind.Sequential)]
  public struct POINT { public int X, Y; }

  [StructLayout(LayoutKind.Sequential)]
  public struct MOUSEINPUT {
    public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct KEYBDINPUT {
    public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo;
  }

  [StructLayout(LayoutKind.Explicit)]
  public struct INPUTUNION {
    [FieldOffset(0)] public MOUSEINPUT mi;
    [FieldOffset(0)] public KEYBDINPUT ki;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct INPUT { public uint type; public INPUTUNION u; }

  public static class Win32 {
    public const uint InputMouse = 0, InputKeyboard = 1;
    public const uint KeyUp = 0x0002, Unicode = 0x0004;
    public const uint MouseMove = 0x0001, Absolute = 0x8000, VirtualDesk = 0x4000;
    public const uint LeftDown = 0x0002, LeftUp = 0x0004, RightDown = 0x0008, RightUp = 0x0010;
    public const int SwRestore = 9;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int t, bool repaint);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] public static extern uint GetOwningProcess(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    public delegate bool EnumProc(IntPtr h, IntPtr l);

    /// A context menu is its own top-level window, so "did the menu open, and where" is a
    /// question about this list rather than about pixels.
    public static string[] TopLevelWindows(uint target) {
      var found = new List<string>();
      EnumWindows((h, l) => {
        uint owner; GetOwningProcess(h, out owner);
        if (owner != target || !IsWindowVisible(h)) { return true; }
        var cls = new StringBuilder(256); GetClassName(h, cls, 256);
        RECT r; GetWindowRect(h, out r);
        found.Add(string.Format("{0}\t{1}\t{2},{3}\t{4}x{5}\t{6}",
          (long)h, cls, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
          h == GetForegroundWindow() ? "foreground" : ""));
        return true;
      }, IntPtr.Zero);
      return found.ToArray();
    }

    // Built here rather than in PowerShell: `$input.u.ki = $ki` reads `$input.u` as a copy of
    // the value type, so every field written through that chain is silently thrown away.
    public static INPUT Key(ushort vk, ushort scan, uint flags) {
      var item = new INPUT();
      item.type = InputKeyboard;
      item.u.ki = new KEYBDINPUT {
        wVk = vk, wScan = scan, dwFlags = flags, time = 0, dwExtraInfo = IntPtr.Zero
      };
      return item;
    }

    public static INPUT Mouse(uint flags, int dx, int dy) {
      var item = new INPUT();
      item.type = InputMouse;
      item.u.mi = new MOUSEINPUT {
        dx = dx, dy = dy, mouseData = 0, dwFlags = flags, time = 0, dwExtraInfo = IntPtr.Zero
      };
      return item;
    }
  }
}
'@
}

# ---------------------------------------------------------------- run state

function Get-RunDirectory {
    if ($Run) {
        $dir = Join-Path $verifyRoot $Run
        if (-not (Test-Path $dir)) { throw "No run '$Run' under $verifyRoot." }
        return $dir
    }
    if (-not (Test-Path $verifyRoot)) { throw "No .verify directory yet. Run 'launch' first." }
    $latest = Get-ChildItem $verifyRoot -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName 'run.json') } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $latest) { throw "No run found under $verifyRoot. Run 'launch' first." }
    return $latest.FullName
}

function Get-RunState {
    $dir = Get-RunDirectory
    $file = Join-Path $dir 'run.json'
    if (-not (Test-Path $file)) { throw "No run.json in $dir. Run 'launch' first." }
    $state = Get-Content $file -Raw | ConvertFrom-Json
    $state | Add-Member -NotePropertyName runDir -NotePropertyValue $dir -Force
    return $state
}

# Times are stored and compared as ticks: ConvertFrom-Json turns an ISO-8601 string back into
# a DateTime, so a string comparison against the stored value never matches.
function Get-OurProcess($state) {
    $process = Get-Process -Id $state.processId -ErrorAction SilentlyContinue
    if (-not $process) { return $null }
    # A dead PID gets reused. Never signal a process just because it has the right number.
    if ($process.ProcessName -ne 'Centaur.App') { return $null }
    if ($process.StartTime.ToUniversalTime().Ticks -ne [int64]$state.startedTicks) { return $null }
    return $process
}

function Get-Hwnd($state) {
    $h = [IntPtr]::new([int64]$state.hwnd)
    if (-not [Centaur.Win32]::IsWindow($h)) {
        throw "The Centaur window for run '$($state.runId)' is gone - the app exited or crashed. Run 'doctor'."
    }
    return $h
}

# ---------------------------------------------------------------- input

function Set-WindowForeground($h) {
    if ([Centaur.Win32]::GetForegroundWindow() -eq $h) { return $true }

    # Windows only grants the foreground to the process that already owns it, so attach to
    # its input queue for the length of the call and hand it over from the inside.
    $foreignThread = [Centaur.Win32]::GetWindowThreadProcessId([Centaur.Win32]::GetForegroundWindow(), [IntPtr]::Zero)
    $ourThread = [Centaur.Win32]::GetCurrentThreadId()

    [void][Centaur.Win32]::AttachThreadInput($ourThread, $foreignThread, $true)
    [void][Centaur.Win32]::ShowWindow($h, [Centaur.Win32]::SwRestore)
    [void][Centaur.Win32]::BringWindowToTop($h)
    [void][Centaur.Win32]::SetForegroundWindow($h)
    [void][Centaur.Win32]::AttachThreadInput($ourThread, $foreignThread, $false)

    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Milliseconds 100
        if ([Centaur.Win32]::GetForegroundWindow() -eq $h) { return $true }
    }
    return $false
}

# SendInput goes to whatever window holds focus. Never send a keystroke unless that window is
# the one this run started, or the recipe gets typed into whatever the user has open.
function Assert-Focused($h) {
    if (Set-WindowForeground $h) { return }
    throw @'
Could not bring this run's Centaur window to the foreground, so no input was sent.
Windows withholds the foreground from a process the user is not interacting with; the usual
causes are a full-screen app, an open Start menu, or a UAC prompt. Clear whatever holds the
foreground - or click the verification window once - and retry the same command.
'@
}

$virtualKeys = @{
    'enter' = 0x0D; 'return' = 0x0D; 'tab' = 0x09; 'escape' = 0x1B; 'esc' = 0x1B
    'space' = 0x20; 'backspace' = 0x08; 'delete' = 0x2E; 'insert' = 0x2D
    'up' = 0x26; 'down' = 0x28; 'left' = 0x25; 'right' = 0x27
    'home' = 0x24; 'end' = 0x23; 'pageup' = 0x21; 'pagedown' = 0x22
    'comma' = 0xBC; 'period' = 0xBE
    'f1' = 0x70; 'f2' = 0x71; 'f3' = 0x72; 'f4' = 0x73; 'f5' = 0x74; 'f6' = 0x75
    'f7' = 0x76; 'f8' = 0x77; 'f9' = 0x78; 'f10' = 0x79; 'f11' = 0x7A; 'f12' = 0x7B
}
$modifierKeys = @{ 'ctrl' = 0x11; 'control' = 0x11; 'shift' = 0x10; 'alt' = 0x12 }

function New-KeyInput([ushort]$vk, [ushort]$scan, [uint32]$flags) {
    return [Centaur.Win32]::Key($vk, $scan, $flags)
}

function New-MouseInput([uint32]$flags, [int]$absX = 0, [int]$absY = 0) {
    return [Centaur.Win32]::Mouse($flags, $absX, $absY)
}

function Send-Inputs($items) {
    $array = [Centaur.INPUT[]]@($items)
    if ($array.Length -eq 0) { return }
    $size = [Runtime.InteropServices.Marshal]::SizeOf([type][Centaur.INPUT])
    $sent = [Centaur.Win32]::SendInput([uint32]$array.Length, $array, $size)
    if ($sent -ne $array.Length) {
        $code = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        throw "SendInput delivered $sent of $($array.Length) events (Win32 error $code). A more privileged window is probably blocking input."
    }
}

function Send-Text([string]$text) {
    $items = [System.Collections.Generic.List[object]]::new()
    foreach ($char in $text.ToCharArray()) {
        # A newline inside -Text means "press Enter", not "type U+000A".
        if ($char -eq "`n") {
            $items.Add((New-KeyInput 0x0D 0 0))
            $items.Add((New-KeyInput 0x0D 0 ([Centaur.Win32]::KeyUp)))
            continue
        }
        if ($char -eq "`r") { continue }
        $code = [ushort][int]$char
        $items.Add((New-KeyInput 0 $code ([Centaur.Win32]::Unicode)))
        $items.Add((New-KeyInput 0 $code ([Centaur.Win32]::Unicode -bor [Centaur.Win32]::KeyUp)))
    }
    Send-Inputs $items
}

function Send-Combo([string]$combo) {
    $parts = @($combo.ToLowerInvariant().Split('+') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($parts.Count -eq 0) { throw "Empty -Combo." }

    $modifiers = @()
    $mainKey = $null
    foreach ($part in $parts) {
        if ($modifierKeys.ContainsKey($part)) { $modifiers += $modifierKeys[$part]; continue }
        if ($null -ne $mainKey) { throw "-Combo '$combo' names more than one non-modifier key." }
        if ($virtualKeys.ContainsKey($part)) { $mainKey = $virtualKeys[$part] }
        elseif ($part.Length -eq 1) { $mainKey = [int][char]$part.ToUpperInvariant() }
        else { throw "Unknown key '$part' in -Combo '$combo'. Known names: $(($virtualKeys.Keys | Sort-Object) -join ', ')." }
    }
    if ($null -eq $mainKey) { throw "-Combo '$combo' has modifiers but no key to press." }

    $items = [System.Collections.Generic.List[object]]::new()
    foreach ($m in $modifiers) { $items.Add((New-KeyInput ([ushort]$m) 0 0)) }
    $items.Add((New-KeyInput ([ushort]$mainKey) 0 0))
    $items.Add((New-KeyInput ([ushort]$mainKey) 0 ([Centaur.Win32]::KeyUp)))
    for ($i = $modifiers.Count - 1; $i -ge 0; $i--) {
        $items.Add((New-KeyInput ([ushort]$modifiers[$i]) 0 ([Centaur.Win32]::KeyUp)))
    }
    Send-Inputs $items
}

function ConvertTo-ScreenPoint($h, [int]$clientX, [int]$clientY) {
    $point = New-Object Centaur.POINT
    $point.X = $clientX; $point.Y = $clientY
    [void][Centaur.Win32]::ClientToScreen($h, [ref]$point)
    return $point
}

function Move-MouseTo([int]$screenX, [int]$screenY) {
    # SendInput absolute coordinates are 0..65535 across some rectangle. VIRTUALDESK is what
    # says that rectangle is the whole virtual desktop; without it Windows measures against the
    # primary monitor alone, and on a multi-monitor desktop every click silently lands at the
    # wrong x - far enough to miss the target, close enough to still hit the window.
    $vLeft = [Centaur.Win32]::GetSystemMetrics(76)
    $vTop = [Centaur.Win32]::GetSystemMetrics(77)
    $vWidth = [Centaur.Win32]::GetSystemMetrics(78)
    $vHeight = [Centaur.Win32]::GetSystemMetrics(79)
    $absX = [int](([double]($screenX - $vLeft) / $vWidth) * 65535)
    $absY = [int](([double]($screenY - $vTop) / $vHeight) * 65535)
    $flags = [Centaur.Win32]::MouseMove -bor [Centaur.Win32]::Absolute -bor [Centaur.Win32]::VirtualDesk
    Send-Inputs (New-MouseInput $flags $absX $absY)
    Start-Sleep -Milliseconds 60
}

function Get-MouseFlags([string]$button) {
    if ($button -eq 'right') {
        return @{ Down = [Centaur.Win32]::RightDown; Up = [Centaur.Win32]::RightUp }
    }
    return @{ Down = [Centaur.Win32]::LeftDown; Up = [Centaur.Win32]::LeftUp }
}

# ---------------------------------------------------------------- capture

# PrintWindow with PW_RENDERFULLCONTENT (2) asks the window to redraw itself into our bitmap.
# It reaches Centaur's ANGLE/Skia surface and works while the window is occluded, so a
# screenshot never depends on what else happens to be on screen.
function Get-WindowBitmap($h) {
    $rect = New-Object Centaur.RECT
    [void][Centaur.Win32]::GetWindowRect($h, [ref]$rect)
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) { throw "The Centaur window has no area ($width x $height)." }

    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [Centaur.Win32]::PrintWindow($h, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    if (-not $ok) { $bitmap.Dispose(); throw "PrintWindow failed on the Centaur window." }
    return $bitmap
}

# Photographs the desktop where the window sits. A context menu is its own top-level popup
# window, so PrintWindow on the main window renders the menu away; only a screen grab shows
# it. The trade is that this captures whatever is physically on top, so it takes the
# foreground first and is padded to catch a menu that spills past the window edge.
function Get-ScreenBitmap($h, $processId, $pad = 240) {
    # An open menu is the foreground window, and it light-dismisses the moment anything
    # activates its owner - including our own SetForegroundWindow. So only take the
    # foreground when it belongs to some other process entirely.
    $owner = 0
    [void][Centaur.Win32]::GetOwningProcess([Centaur.Win32]::GetForegroundWindow(), [ref]$owner)
    if ($owner -ne $processId) {
        if (-not (Set-WindowForeground $h)) {
            throw "Could not bring the Centaur window to the foreground, so a screen capture would photograph the wrong window. Use 'shot' without -Screen if you do not need popups."
        }
        Start-Sleep -Milliseconds 150
    }

    $rect = New-Object Centaur.RECT
    [void][Centaur.Win32]::GetWindowRect($h, [ref]$rect)
    $left = $rect.Left - $pad
    $top = $rect.Top - $pad
    $width = ($rect.Right - $rect.Left) + (2 * $pad)
    $height = ($rect.Bottom - $rect.Top) + (2 * $pad)

    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try { $graphics.CopyFromScreen($left, $top, 0, 0, $bitmap.Size) } finally { $graphics.Dispose() }
    return $bitmap
}

# ---------------------------------------------------------------- commands

function Invoke-Build {
    $project = Join-Path $repoRoot 'src\Centaur.App\Centaur.App.csproj'
    & dotnet build $project -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }
    if (-not (Test-Path $exePath)) { throw "Build reported success but $exePath is missing." }
    Write-Host "Built $exePath"
}

function Invoke-Launch {
    if (-not (Test-Path $exePath)) {
        throw "$exePath is missing. Run 'build' first."
    }

    $runId = if ($Run) { $Run } else { 'run-' + (Get-Date -Format 'yyyyMMdd-HHmmss') }
    $runDir = Join-Path $verifyRoot $runId
    if (Test-Path (Join-Path $runDir 'run.json')) { throw "Run '$runId' already exists at $runDir." }

    $configDir = Join-Path $runDir 'config'
    $workDir = Join-Path $runDir 'workdir'
    $artifactsDir = Join-Path $runDir 'artifacts'
    foreach ($dir in $runDir, $configDir, $workDir, $artifactsDir) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    # Start in the run's scratch directory rather than wherever the user last was, so a
    # command typed into the terminal writes somewhere this run owns.
    @{
        StartDirectory = 'SpecificFolder'
        SpecificFolder = $workDir
        LastFolder     = $workDir
    } | ConvertTo-Json | Set-Content (Join-Path $configDir 'settings.json') -Encoding utf8

    # The child inherits this. Everything the instance persists lands in the run directory,
    # which is what keeps a verification run off the user's own tabs and history.
    $previous = $env:CENTAUR_CONFIG_DIR
    $env:CENTAUR_CONFIG_DIR = $configDir
    try {
        $process = Start-Process $exePath -PassThru
    } finally {
        if ($null -eq $previous) { Remove-Item Env:\CENTAUR_CONFIG_DIR -ErrorAction SilentlyContinue }
        else { $env:CENTAUR_CONFIG_DIR = $previous }
    }

    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) { throw "Centaur exited during startup with code $($process.ExitCode)." }
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 250
    }
    if ($process.MainWindowHandle -eq [IntPtr]::Zero) {
        throw "Centaur started (PID $($process.Id)) but showed no window within 30s."
    }

    $h = $process.MainWindowHandle
    # Let the window finish restoring its (empty) session and spawn the first pane's shell
    # before resizing, so the resize lands after the restore and is not swallowed by it.
    Start-Sleep -Milliseconds 2500
    # A fixed window size is what makes the click coordinates in the feature map mean anything.
    [void][Centaur.Win32]::MoveWindow($h, $Left, $Top, $Width, $Height, $true)

    # That resize also schedules the app's first session save (debounced ~400ms), which is how
    # this run proves CENTAUR_CONFIG_DIR took effect: the file has to appear in the run's own
    # config directory. If it never does, the instance is writing the user's state instead and
    # must not be driven.
    $ourSession = Join-Path $configDir 'session.json'
    $sessionDeadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $sessionDeadline -and -not (Test-Path $ourSession)) {
        Start-Sleep -Milliseconds 250
    }
    if (-not (Test-Path $ourSession)) {
        $orphan = $process.Id
        try { [void]$process.CloseMainWindow(); [void]$process.WaitForExit(5000) } catch { }
        throw @"
Centaur (PID $orphan) never wrote $ourSession, so it is not using this run's config directory
and would read and write the user's own tabs, history and settings in $userConfigDir.
The instance was closed rather than driven.

The usual cause is an app build that predates CENTAUR_CONFIG_DIR support in
src/Centaur.Core/Terminal/ConfigPaths.cs. Run 'build' and launch again.
"@
    }
    Start-Sleep -Milliseconds 500

    $userSession = Join-Path $userConfigDir 'session.json'
    $state = [pscustomobject]@{
        runId            = $runId
        processId        = $process.Id
        hwnd             = [int64]$h
        exePath          = $exePath
        exeBuiltTicks    = (Get-Item $exePath).LastWriteTimeUtc.Ticks
        startedTicks     = $process.StartTime.ToUniversalTime().Ticks
        launchedAt       = (Get-Date).ToUniversalTime().ToString('o')
        configDir        = $configDir
        workDir          = $workDir
        artifactsDir     = $artifactsDir
        windowSize       = "$Width x $Height"
        # Recorded so doctor can tell whether the user's own config moved under us.
        userSessionPath  = $userSession
        userSessionTicks = if (Test-Path $userSession) { (Get-Item $userSession).LastWriteTimeUtc.Ticks } else { 0 }
    }
    $state | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $runDir 'run.json') -Encoding utf8

    # Foreground alone does not give the pane keyboard focus: TerminalControl takes focus on
    # pointer press, so an un-clicked window swallows everything typed at it. Click once into
    # the first pane, which is what a user does to a window they have just opened anyway.
    if (Set-WindowForeground $h) {
        $point = ConvertTo-ScreenPoint $h 400 300
        Move-MouseTo $point.X $point.Y
        Send-Inputs @((New-MouseInput ([Centaur.Win32]::LeftDown)), (New-MouseInput ([Centaur.Win32]::LeftUp)))
        Start-Sleep -Milliseconds 400
    } else {
        Write-Host "Note: could not focus the new window, so the first pane has no keyboard focus yet. Run 'focus' before typing." -ForegroundColor Yellow
    }

    [pscustomobject]@{
        runId     = $runId
        processId = $state.processId
        hwnd      = $state.hwnd
        workDir   = $workDir
        artifacts = $artifactsDir
    } | Format-List
}

function Invoke-Doctor {
    $state = Get-RunState
    $problems = @()

    $process = Get-OurProcess $state
    if (-not $process) {
        $problems += "The process this run started (PID $($state.processId), launched $($state.launchedAt)) is no longer running."
    }

    $h = [IntPtr]::new([int64]$state.hwnd)
    $windowGeometry = 'n/a'
    if (-not [Centaur.Win32]::IsWindow($h)) {
        $problems += "Window $($state.hwnd) no longer exists."
    } elseif (-not [Centaur.Win32]::IsWindowVisible($h)) {
        $problems += "Window $($state.hwnd) exists but is not visible."
    } else {
        $rect = New-Object Centaur.RECT
        [void][Centaur.Win32]::GetWindowRect($h, [ref]$rect)
        $windowGeometry = "$($rect.Right - $rect.Left)x$($rect.Bottom - $rect.Top) at $($rect.Left),$($rect.Top)"
    }

    if (Test-Path $state.exePath) {
        if ((Get-Item $state.exePath).LastWriteTimeUtc.Ticks -ne [int64]$state.exeBuiltTicks) {
            $problems += "$($state.exePath) was rebuilt after launch. The running window is the OLD build - stop this run and launch again."
        }
    } else {
        $problems += "$($state.exePath) is missing."
    }

    if (-not (Test-Path $state.workDir)) { $problems += "Scratch working directory $($state.workDir) is missing." }

    # Positive proof that CENTAUR_CONFIG_DIR took effect: the app persists its layout shortly
    # after the first tab opens, so its session file must exist in the run's own config
    # directory. Checking that the user's file is merely *unchanged* would not do - the user's
    # own instance writes that file legitimately, and an unwritten file proves nothing.
    $ourSession = Join-Path $state.configDir 'session.json'
    $configActive = Test-Path $ourSession
    if (-not $configActive) {
        $problems += "$ourSession does not exist, so this instance is not using the run's config directory. It may be reading and writing the user's own state in $userConfigDir - stop it before driving anything."
    }

    # Other instances are fine now that config is per-run, but the agent should know they exist.
    $others = @(Get-Process -Name 'Centaur.App' -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne $state.processId })

    # Only suspicious when nothing else could have written it.
    $userTicks = if (Test-Path $state.userSessionPath) { (Get-Item $state.userSessionPath).LastWriteTimeUtc.Ticks } else { 0 }
    $userConfigMoved = ($userTicks -ne [int64]$state.userSessionTicks)
    if ($userConfigMoved -and $others.Count -eq 0) {
        $problems += "$($state.userSessionPath) changed since launch and no other Centaur is running, so this run wrote it. The config override is not working."
    }

    [pscustomobject]@{
        runId           = $state.runId
        processId       = $state.processId
        hwnd            = $state.hwnd
        window          = $windowGeometry
        configDir       = $state.configDir
        configInUse     = $configActive
        workDir         = $state.workDir
        userConfigMoved = if ($userConfigMoved) { "yes - explained by other instances ($($others.Id -join ', '))" } else { 'no' }
        otherInstances  = if ($others.Count -gt 0) { $others.Id -join ', ' } else { 'none' }
        healthy         = ($problems.Count -eq 0)
    } | Format-List

    if ($problems.Count -gt 0) {
        Write-Host 'PROBLEMS' -ForegroundColor Red
        $problems | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
        exit 1
    }
    Write-Host 'Healthy: this instance is worth driving.' -ForegroundColor Green
}

function Invoke-Shot {
    if (-not $Name) { throw "shot needs -Name." }
    $state = Get-RunState
    $h = Get-Hwnd $state
    $path = Join-Path $state.artifactsDir "$Name.png"
    New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
    $bitmap = if ($Screen) { Get-ScreenBitmap $h $state.processId } else { Get-WindowBitmap $h }
    try { $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png) } finally { $bitmap.Dispose() }
    Write-Host $path
}

function Invoke-Pixel {
    $state = Get-RunState
    $h = Get-Hwnd $state
    # Sample the window bitmap but take the coordinate in the same client space as click,
    # so a colour check and the click that produced it can share numbers.
    $rect = New-Object Centaur.RECT
    [void][Centaur.Win32]::GetWindowRect($h, [ref]$rect)
    $origin = ConvertTo-ScreenPoint $h 0 0
    $bitmapX = $X + ($origin.X - $rect.Left)
    $bitmapY = $Y + ($origin.Y - $rect.Top)

    $bitmap = Get-WindowBitmap $h
    try {
        if ($bitmapX -lt 0 -or $bitmapY -lt 0 -or $bitmapX -ge $bitmap.Width -or $bitmapY -ge $bitmap.Height) {
            throw "Client point $X,$Y falls outside the window."
        }
        $color = $bitmap.GetPixel($bitmapX, $bitmapY)
        [pscustomobject]@{
            clientX = $X
            clientY = $Y
            hex     = '#{0:X2}{1:X2}{2:X2}' -f $color.R, $color.G, $color.B
            r       = $color.R
            g       = $color.G
            b       = $color.B
        } | Format-List
    } finally { $bitmap.Dispose() }
}

function Invoke-State {
    $state = Get-RunState
    foreach ($file in 'session.json', 'settings.json', 'command-history.json') {
        $path = Join-Path $state.configDir $file
        Write-Host "--- $path"
        if (Test-Path $path) { Get-Content $path -Raw } else { Write-Host '(not written yet)' }
        Write-Host ''
    }
}

function Invoke-Stop {
    $state = Get-RunState
    $process = Get-OurProcess $state
    if (-not $process) {
        Write-Host "Nothing to stop: PID $($state.processId) is not the process this run started."
        return
    }
    # CloseMainWindow, not Kill, so the app runs its Closed handler and flushes the session -
    # which is what a persistence check needs to observe.
    [void]$process.CloseMainWindow()
    if (-not $process.WaitForExit(10000)) {
        Write-Host "Centaur did not close within 10s; killing PID $($state.processId)." -ForegroundColor Yellow
        $process.Kill()
        [void]$process.WaitForExit(5000)
    }
    Write-Host "Stopped PID $($state.processId)."
}

function Invoke-Cleanup {
    $state = Get-RunState
    Invoke-Stop

    # Nothing to restore: the run never owned anything outside its own directory. The config
    # and workdir stay put - they are evidence of what the run persisted and wrote - and so
    # do the artifacts.
    $others = @(Get-Process -Name 'Centaur.App' -ErrorAction SilentlyContinue)
    if ($others.Count -gt 0) {
        Write-Host "Note: Centaur is still running (PID $($others.Id -join ', ')). This run did not start it; leaving it alone." -ForegroundColor Yellow
    }

    Write-Host "Evidence kept at $($state.artifactsDir)"
    Write-Host "Persisted state kept at $($state.configDir)"
}

switch ($Command) {
    'build' { Invoke-Build }
    'launch' { Invoke-Launch }
    'doctor' { Invoke-Doctor }
    'focus' {
        # Window foreground plus a click into the pane, because Avalonia only moves keyboard
        # focus to the terminal on pointer press. Use -X/-Y to focus a specific pane.
        $h = Get-Hwnd (Get-RunState)
        Assert-Focused $h
        $paneX = if ($X -ne 0) { $X } else { 400 }
        $paneY = if ($Y -ne 0) { $Y } else { 300 }
        $point = ConvertTo-ScreenPoint $h $paneX $paneY
        Move-MouseTo $point.X $point.Y
        Send-Inputs @((New-MouseInput ([Centaur.Win32]::LeftDown)), (New-MouseInput ([Centaur.Win32]::LeftUp)))
        Start-Sleep -Milliseconds $Settle
        Write-Host "Foregrounded and clicked the pane at client $paneX,$paneY."
    }
    'type' {
        if ($null -eq $Text) { throw "type needs -Text." }
        Assert-Focused (Get-Hwnd (Get-RunState))
        Send-Text $Text
        Start-Sleep -Milliseconds $Settle
    }
    'key' {
        if (-not $Combo) { throw "key needs -Combo." }
        Assert-Focused (Get-Hwnd (Get-RunState))
        # Repeats go one at a time with a gap: a menu that is still animating open drops a
        # burst of arrow keys and leaves the highlight one row down instead of four.
        for ($i = 0; $i -lt [Math]::Max(1, $Count); $i++) {
            Send-Combo $Combo
            if ($i -lt $Count - 1) { Start-Sleep -Milliseconds 80 }
        }
        Start-Sleep -Milliseconds $Settle
    }
    'click' {
        $h = Get-Hwnd (Get-RunState)
        Assert-Focused $h
        $point = ConvertTo-ScreenPoint $h $X $Y
        Move-MouseTo $point.X $point.Y
        $flags = Get-MouseFlags $Button
        for ($i = 0; $i -lt $Count; $i++) {
            Send-Inputs @((New-MouseInput $flags.Down), (New-MouseInput $flags.Up))
            if ($i -lt $Count - 1) { Start-Sleep -Milliseconds 80 }
        }
        Start-Sleep -Milliseconds $Settle
    }
    'drag' {
        $h = Get-Hwnd (Get-RunState)
        Assert-Focused $h
        $from = ConvertTo-ScreenPoint $h $X $Y
        $to = ConvertTo-ScreenPoint $h $ToX $ToY
        $flags = Get-MouseFlags $Button
        Move-MouseTo $from.X $from.Y
        Send-Inputs (New-MouseInput $flags.Down)
        # Step the move: a single jump reads as a teleport and drag handlers can miss it.
        for ($step = 1; $step -le 8; $step++) {
            Move-MouseTo ($from.X + [int](($to.X - $from.X) * $step / 8)) ($from.Y + [int](($to.Y - $from.Y) * $step / 8))
        }
        Send-Inputs (New-MouseInput $flags.Up)
        Start-Sleep -Milliseconds $Settle
    }
    'shot' { Invoke-Shot }
    'pixel' { Invoke-Pixel }
    'windows' {
        $state = Get-RunState
        Write-Host "hwnd`tclass`torigin`tsize`tfocus"
        [Centaur.Win32]::TopLevelWindows([uint32]$state.processId) | ForEach-Object { Write-Host $_ }
    }
    'state' { Invoke-State }
    'workdir' { (Get-RunState).workDir }
    'artifacts' { (Get-RunState).artifactsDir }
    'stop' { Invoke-Stop }
    'cleanup' { Invoke-Cleanup }
}
