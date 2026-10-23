# 4.7b, R.1a: drives an installed keypaste desktop app from outside its process through UI Automation (D-0199).
# One verb per call, as scripts/lib/desktop-drive.sh defines them; prints what it saw and exits 1 when the verb
# could not be done, 2 when this driver does not implement it. DRIVE_WINDOW names the app's window to act in, and
# DRIVE_TREE_DIR, when set, receives the window's automation tree each time a locator finds nothing.
param(
  [Parameter(Mandatory = $true)][string]$Action,
  [Parameter(Mandatory = $true)][int]$AppPid,
  [string]$First = '',
  [string]$Second = '',
  [string]$Third = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
# A name with an ellipsis reaches bash as UTF-8; a runner's shell may have no console to set it on.
# This file stays ASCII, since Windows PowerShell reads a script without a byte-order mark as ANSI.
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

Add-Type -Name Native -Namespace Keypaste -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
'@
# UI Automation reports physical pixels, which the cursor and the screen capture then use too.
[void][Keypaste.Native]::SetProcessDPIAware()

$Auto = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$Types = [System.Windows.Automation.ControlType]
$Walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
$RawWalker = [System.Windows.Automation.TreeWalker]::RawViewWalker
$Title = if ($env:DRIVE_WINDOW) { $env:DRIVE_WINDOW } else { 'keypaste' }

# Avalonia exposes a panel that carries a name, such as a recommendation's row, only in the raw view.
$RawView = New-Object System.Windows.Automation.CacheRequest
$RawView.TreeFilter = [System.Windows.Automation.Automation]::RawViewCondition
$RawView.AutomationElementMode = [System.Windows.Automation.AutomationElementMode]::Full

$Patterns = @{
  Invoke = $Auto::IsInvokePatternAvailableProperty
  Toggle = $Auto::IsTogglePatternAvailableProperty
  Value = $Auto::IsValuePatternAvailableProperty
}

function Fail([string]$why) { Write-Output $why; exit 1 }

function Unimplemented([string]$what) { Write-Output "$what not implemented on windows"; exit 2 }

# Polls the probe until it gives something; an element that vanishes while it is read only means not yet.
function Until([int]$seconds, [scriptblock]$probe) {
  $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
  do {
    $found = $null
    try { $found = & $probe } catch { $found = $null }
    if ($found) { return $found }
    Start-Sleep -Milliseconds 250
  } while ([DateTime]::UtcNow -lt $deadline)
  return $null
}

$Mine = New-Object System.Windows.Automation.PropertyCondition($Auto::ProcessIdProperty, $AppPid)
$IsWindow = New-Object System.Windows.Automation.PropertyCondition($Auto::ControlTypeProperty, $Types::Window)

function TopWindows { @($Auto::RootElement.FindAll($Scope::Children, (New-Object System.Windows.Automation.AndCondition($Mine, $IsWindow)))) }

# A dialog its owner holds, such as a wizard, a message box or a file dialog, sits under that window.
function Window {
  $named = New-Object System.Windows.Automation.PropertyCondition($Auto::NameProperty, $Title)
  $top = $Auto::RootElement.FindFirst($Scope::Children, (New-Object System.Windows.Automation.AndCondition($Mine, $IsWindow, $named)))
  if ($top) { return $top }
  foreach ($owner in (TopWindows)) {
    $owned = $owner.FindFirst($Scope::Descendants, (New-Object System.Windows.Automation.AndCondition($IsWindow, $named)))
    if ($owned) { return $owned }
  }
  return $null
}

# Whether the keyboard focus is on the element or inside it, as it is in a field a focus proxy hands it to.
function Holds($element) {
  $focused = $Auto::FocusedElement
  for ($i = 0; $focused -and $i -lt 100; $i++) {
    if ([System.Windows.Automation.Automation]::Compare($focused, $element)) { return $true }
    $focused = $RawWalker.GetParent($focused)
  }
  return $false
}

function RawDescendants($root, $condition) {
  $active = $RawView.Activate()
  try { return @($root.FindAll($Scope::Descendants, $condition)) } finally { $active.Dispose() }
}

function Has($element, [string]$pattern) { [bool]$element.GetCurrentPropertyValue($Patterns[$pattern]) }

function Kind($element) { $element.Current.ControlType.ProgrammaticName }

# What each verb can act on; a locator's [n] counts only these.
$Any = { param($e) $true }
$Text = { param($e) $e.Current.ControlType -eq $Types::Text }
$Invokable = { param($e) (Has $e Invoke) -or (Has $e Toggle) }
$Toggleable = { param($e) Has $e Toggle }
$Settable = { param($e) Has $e Value }

# A locator is a name, or id:<automation id>, either with [n] for the nth element in tree order that the verb can
# act on.
function Named([string]$locator, [scriptblock]$fits) {
  $window = Window
  if (-not $window) { return $null }
  $index = 1
  if ($locator -match '^(.*)\[(\d+)\]$') { $locator = $Matches[1]; $index = [int]$Matches[2] }
  $condition = if ($locator.StartsWith('id:')) {
    New-Object System.Windows.Automation.PropertyCondition($Auto::AutomationIdProperty, $locator.Substring(3))
  } else {
    New-Object System.Windows.Automation.PropertyCondition($Auto::NameProperty, $locator)
  }
  $seen = 0
  foreach ($element in (RawDescendants $window $condition)) {
    if (& $fits $element) {
      $seen++
      if ($seen -eq $index) { return $element }
    }
  }
  return $null
}

# The window's raw tree, for diagnosing a locator that found nothing. It holds names, never a field's value.
function Dump {
  if (-not $env:DRIVE_TREE_DIR) { return '' }
  try {
    $window = Window
    if (-not $window) { return '' }
    $lines = New-Object System.Collections.Generic.List[string]
    Walk $window 0 $lines
    $file = Join-Path $env:DRIVE_TREE_DIR ('tree-{0:yyyyMMdd-HHmmss-fff}-{1}.txt' -f [DateTime]::UtcNow, $Action)
    Set-Content -LiteralPath $file -Value $lines -Encoding UTF8
    return "; the window's automation tree is in $(Split-Path -Leaf $file)"
  } catch {
    return "; the window's automation tree could not be written: $($_.Exception.Message)"
  }
}

function Walk($element, [int]$depth, $lines) {
  if ($depth -gt 60) { return }
  $current = $element.Current
  $name = $current.Name
  if ($name.Length -gt 120) { $name = $name.Substring(0, 120) + '...' }
  $flags = @()
  if ($current.AutomationId) { $flags += "id=$($current.AutomationId)" }
  if (-not $current.IsEnabled) { $flags += 'disabled' }
  if ($current.IsOffscreen) { $flags += 'offscreen' }
  if ($current.HasKeyboardFocus) { $flags += 'focused' }
  $lines.Add(('{0}{1} ''{2}'' {3}' -f ('  ' * $depth), ($current.ControlType.ProgrammaticName -replace '^ControlType\.', ''), $name, ($flags -join ' ')))
  $child = $RawWalker.GetFirstChild($element)
  while ($child) {
    Walk $child ($depth + 1) $lines
    $child = $RawWalker.GetNextSibling($child)
  }
}

function Missing([string]$why) { Fail ($why + (Dump)) }

function Enabled($element, [string]$locator) {
  if (-not (Until 30 { if ($element.Current.IsEnabled) { $true } })) { Fail "'$locator' stayed disabled for 30 s" }
}

# Avalonia's window element refuses UIA SetFocus, so the native window is brought forward instead. Windows
# grants SetForegroundWindow only to the process that last had input, which a released Alt key gives this one;
# a window already in front is left alone, since the Alt key would reach it.
function Foreground {
  $window = Until 30 { Window }
  if (-not $window) { Fail "no window '$Title' to send input to" }
  $handle = [IntPtr]$window.Current.NativeWindowHandle
  if ([Keypaste.Native]::GetForegroundWindow() -ne $handle) {
    [Keypaste.Native]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
    [Keypaste.Native]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
    [void][Keypaste.Native]::SetForegroundWindow($handle)
    if (-not (Until 5 { if ([Keypaste.Native]::GetForegroundWindow() -eq $handle) { $true } })) {
      $front = $null
      try { $front = $Auto::FromHandle([Keypaste.Native]::GetForegroundWindow()).Current } catch { }
      Fail "the window '$Title' did not come to the foreground; '$($front.Name)' of process $($front.ProcessId) holds it"
    }
  }
}

# SendKeys' spelling of a chord; letters go lower case, since an upper-case one would add Shift.
function Chord([string]$chord) {
  $named = @{ Return = '{ENTER}'; Escape = '{ESC}'; Tab = '{TAB}'; Delete = '{DEL}'; BackSpace = '{BACKSPACE}'; Home = '{HOME}'; End = '{END}'
    Up = '{UP}'; Down = '{DOWN}'; Left = '{LEFT}'; Right = '{RIGHT}'; space = ' ' }
  $modifiers = @{ ctrl = '^'; shift = '+'; alt = '%' }
  $prefix = ''
  $key = $chord
  while ($key -match '^(ctrl|shift|alt)\+(.+)$') { $prefix += $modifiers[$Matches[1]]; $key = $Matches[2] }
  if ($named.ContainsKey($key)) { return $prefix + $named[$key] }
  if ($key -match '^[A-Za-z0-9]$') { return $prefix + $key.ToLowerInvariant() }
  Unimplemented "the chord $chord"
}

function Click($element, [string]$locator, [uint32]$down, [uint32]$up) {
  Foreground
  $r = $element.Current.BoundingRectangle
  if ($r.IsEmpty -or $r.Width -le 0 -or $r.Height -le 0 -or $element.Current.IsOffscreen) { Fail "'$locator' is not on screen: $r" }
  $x = [int]($r.X + $r.Width / 2)
  $y = [int]($r.Y + $r.Height / 2)
  [void][Keypaste.Native]::SetCursorPos($x, $y)
  [Keypaste.Native]::mouse_event($down, 0, 0, 0, [UIntPtr]::Zero)
  [Keypaste.Native]::mouse_event($up, 0, 0, 0, [UIntPtr]::Zero)
  return "$x,$y"
}

switch ($Action) {
  'window' {
    $window = Until ([int]$First) { Window }
    if (-not $window) { Fail "no window '$Title' for process $AppPid within $First s" }
    Write-Output "window '$($window.Current.Name)' $($window.Current.BoundingRectangle)"
  }
  'windows' {
    $found = TopWindows
    if ($found.Count -eq 0) { Fail "process $AppPid shows no window" }
    foreach ($window in $found) {
      Write-Output $window.Current.Name
      foreach ($owned in @($window.FindAll($Scope::Descendants, $IsWindow))) { Write-Output $owned.Current.Name }
    }
  }
  'screenshot' {
    $window = Until 10 { Window }
    if (-not $window) { Fail 'no window to capture' }
    $r = $window.Current.BoundingRectangle
    $bitmap = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bitmap.Size)
    $bitmap.Save($First, [System.Drawing.Imaging.ImageFormat]::Png)
    $colours = New-Object 'System.Collections.Generic.HashSet[int]'
    for ($x = 0; $x -lt $bitmap.Width; $x += 4) {
      for ($y = 0; $y -lt $bitmap.Height; $y += 4) { [void]$colours.Add($bitmap.GetPixel($x, $y).ToArgb()) }
    }
    Write-Output "colours=$($colours.Count) size=$($bitmap.Width)x$($bitmap.Height)"
  }
  'type' {
    Foreground
    [System.Windows.Forms.SendKeys]::SendWait(($First -replace '[+^%~(){}\[\]]', '{$0}'))
    Write-Output "typed $($First.Length) characters"
  }
  'key' {
    $keys = Chord $First
    Foreground
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Write-Output "sent $First"
  }
  'key-wait' {
    $keys = Chord $First
    $seconds = [int]$Third
    Foreground
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    # Each look walks the window, so the time is bounded by the last look that missed and the one that found it.
    $unseen = 0
    $found = $null
    while ($clock.Elapsed.TotalSeconds -lt $seconds) {
      $looked = $clock.ElapsedMilliseconds
      try { $found = Named $Second $Any } catch { $found = $null }
      if ($found) { break }
      $unseen = $looked
      Start-Sleep -Milliseconds 50
    }
    $elapsed = $clock.ElapsedMilliseconds
    if (-not $found) { Missing "nothing named '$Second' appeared within $seconds s of $First" }
    Write-Output "found '$Second' $elapsed ms after $First (not yet at $unseen ms)"
  }
  'find' {
    $element = Until 30 { Named $First $Any }
    if (-not $element) { Missing "nothing named '$First' appeared" }
    Write-Output "found '$First' as $(Kind $element)"
  }
  'find-prefix' {
    $text = Until 30 {
      $window = Window
      if ($window) {
        foreach ($element in (RawDescendants $window ([System.Windows.Automation.Condition]::TrueCondition))) {
          if ($element.Current.Name.StartsWith($First)) { return $element.Current.Name }
        }
      }
    }
    if (-not $text) { Missing "nothing starting '$First' appeared" }
    Write-Output $text
  }
  'wait' {
    $element = Until ([int]$Second) { Named $First $Any }
    if (-not $element) { Missing "nothing named '$First' appeared within $Second s" }
    Write-Output "found '$First' as $(Kind $element)"
  }
  'wait-gone' {
    $gone = Until ([int]$Second) { if (-not (Named $First $Any)) { $true } }
    if (-not $gone) { Fail "'$First' was still there after $Second s" }
    Write-Output "gone '$First'"
  }
  'read' {
    $element = Until 30 { Named $First $Any }
    if (-not $element) { Missing "nothing named '$First' to read" }
    if (Has $element Value) {
      Write-Output $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    } else {
      Write-Output $element.Current.Name
    }
  }
  'select' {
    $element = Until 30 { Named $First $Text }
    if (-not $element) { Missing "no text '$First' to select" }
    $item = $element
    while ($item -and $item.Current.ControlType -ne $Types::ListItem) { $item = $Walker.GetParent($item) }
    if (-not $item) { Fail "'$First' is not inside a list item" }
    Enabled $item $First
    $selection = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $selection.Select()
    if (-not (Until 5 { if ($selection.Current.IsSelected) { $true } })) { Fail "the list item holding '$First' did not become selected" }
    Write-Output "selected '$First'"
  }
  'select-nth' {
    $index = [int]$Second
    $listItem = New-Object System.Windows.Automation.PropertyCondition($Auto::ControlTypeProperty, $Types::ListItem)
    $item = Until 30 {
      $list = Named $First $Any
      if ($list) {
        $items = @($list.FindAll($Scope::Descendants, $listItem))
        if ($items.Count -ge $index) { $items[$index - 1] }
      }
    }
    if (-not $item) { Missing "'$First' holds no item $index" }
    Enabled $item "$First item $index"
    $selection = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $selection.Select()
    if (-not (Until 5 { if ($selection.Current.IsSelected) { $true } })) { Fail "item $index of '$First' did not become selected" }
    Write-Output "selected item $index of '$First'"
  }
  'invoke' {
    $element = Until 30 { Named $First $Invokable }
    if (-not $element) { Missing "no button '$First'" }
    Enabled $element $First
    try {
      if (Has $element Invoke) {
        $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
      } else {
        $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
      }
      Write-Output "invoked '$First'"
    } catch {
      # Qt presses a tool button within the call, so a modal window the press opens, such as a file dialog, holds the
      # call past UI Automation's timeout; the window that opened is the next verb's to find.
      if ("$($_.Exception.InnerException.HResult)" -ne '-2146233083') { throw }
      Write-Output "invoked '$First', which had not returned when UI Automation's timeout passed"
    }
  }
  'toggle' {
    $element = Until 30 { Named $First $Toggleable }
    if (-not $element) { Missing "no toggle '$First'" }
    Enabled $element $First
    $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Write-Output "toggled '$First'"
  }
  'check' {
    $want = switch ($Second) { 'on' { 'On' } 'off' { 'Off' } default { Fail "check takes on or off, not '$Second'" } }
    $element = Until 30 { Named $First $Toggleable }
    if (-not $element) { Missing "no check box '$First'" }
    $toggle = $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if ("$($toggle.Current.ToggleState)" -ne $want) {
      Enabled $element $First
      $toggle.Toggle()
    }
    if (-not (Until 5 { if ("$($toggle.Current.ToggleState)" -eq $want) { $true } })) { Fail "'$First' is $($toggle.Current.ToggleState), not $Second" }
    Write-Output "'$First' is $Second"
  }
  'click' {
    $element = Until 30 { Named $First $Any }
    if (-not $element) { Missing "nothing named '$First' to click" }
    Write-Output "clicked '$First' at $(Click $element $First 0x2 0x4)"
  }
  'right-click' {
    $element = Until 30 { Named $First $Any }
    if (-not $element) { Missing "nothing named '$First' to right-click" }
    Write-Output "right-clicked '$First' at $(Click $element $First 0x8 0x10)"
  }
  'focus' {
    $element = Until 30 { Named $First $Any }
    if (-not $element) { Missing "nothing named '$First' to focus" }
    Enabled $element $First
    Foreground
    $element.SetFocus()
    if (-not (Until 5 { if (Holds $element) { $true } })) { Fail "'$First' did not take the keyboard focus" }
    Write-Output "focused '$First'"
  }
  'set-after' {
    $label = Until 30 { Named $First $Text }
    if (-not $label) { Missing "no label '$First'" }
    $edit = $Walker.GetNextSibling($label)
    while ($edit -and -not (Has $edit Value)) { $edit = $Walker.GetNextSibling($edit) }
    if (-not $edit) { Fail "no field after '$First'" }
    $value = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $value.SetValue($Second)
    if ($value.Current.Value -ne $Second) { Fail "the field after '$First' reads '$($value.Current.Value)'" }
    Write-Output "set the field after '$First'"
  }
  'set-named' {
    $edit = Until 30 { Named $First $Settable }
    if (-not $edit) { Missing "no field named '$First'" }
    Enabled $edit $First
    $value = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $value.SetValue($Second)
    if ($value.Current.Value -ne $Second) { Fail "the field named '$First' reads '$($value.Current.Value)'" }
    Write-Output "set the field named '$First'"
  }
  default { Fail "no verb named $Action" }
}
