# 4.7b: drives an installed keypaste desktop app from outside its process through UI Automation (D-0199).
# One action per call; prints what it saw and exits non-zero when the action could not be done.
param(
  [Parameter(Mandatory = $true)][string]$Action,
  [Parameter(Mandatory = $true)][int]$AppPid,
  [string]$First = '',
  [string]$Second = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing

$Auto = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$Walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker

function Fail([string]$why) { Write-Output $why; exit 1 }

function Until([int]$seconds, [scriptblock]$probe) {
  $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
  do {
    $found = & $probe
    if ($found) { return $found }
    Start-Sleep -Milliseconds 250
  } while ([DateTime]::UtcNow -lt $deadline)
  return $null
}

function Window {
  $condition = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition($Auto::ProcessIdProperty, $AppPid)),
    (New-Object System.Windows.Automation.PropertyCondition($Auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)))
  $Auto::RootElement.FindFirst($Scope::Children, $condition)
}

function Named([string]$name, [System.Windows.Automation.ControlType]$type) {
  $window = Window
  if (-not $window) { return $null }
  $condition = New-Object System.Windows.Automation.PropertyCondition($Auto::NameProperty, $name)
  foreach ($element in $window.FindAll($Scope::Descendants, $condition)) {
    if (-not $type -or $element.Current.ControlType -eq $type) { return $element }
  }
  return $null
}

Add-Type -Name Foreground -Namespace Keypaste -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
'@

# Avalonia's window element refuses UIA SetFocus, so the native window is brought forward instead. Windows
# grants SetForegroundWindow only to the process that last had input, which a released Alt key gives this one.
function Foreground {
  $window = Until 30 { Window }
  if (-not $window) { Fail 'no window to send keys to' }
  $handle = [IntPtr]$window.Current.NativeWindowHandle
  [Keypaste.Foreground]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
  [Keypaste.Foreground]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
  [void][Keypaste.Foreground]::SetForegroundWindow($handle)
  if ([Keypaste.Foreground]::GetForegroundWindow() -ne $handle) { Fail 'the app window did not come to the foreground' }
}

switch ($Action) {
  'window' {
    $window = Until ([int]$First) { Window }
    if (-not $window) { Fail "no window for process $AppPid within $First s" }
    Write-Output "window '$($window.Current.Name)' $($window.Current.BoundingRectangle)"
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
    [System.Windows.Forms.SendKeys]::SendWait($First)
    Write-Output "typed $($First.Length) characters"
  }
  'key' {
    $chord = switch -Regex ($First) {
      '^Return$' { '{ENTER}' }
      '^ctrl\+(\d)$' { '^' + $Matches[1] }
      default { Fail "no chord named $First" }
    }
    Foreground
    [System.Windows.Forms.SendKeys]::SendWait($chord)
    Write-Output "sent $First"
  }
  'find' {
    $element = Until 30 { Named $First $null }
    if (-not $element) { Fail "nothing named '$First' appeared" }
    Write-Output "found '$First' as $($element.Current.ControlType.ProgrammaticName)"
  }
  'find-prefix' {
    $text = Until 30 {
      $window = Window
      if ($window) {
        foreach ($element in $window.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
          if ($element.Current.Name.StartsWith($First)) { return $element.Current.Name }
        }
      }
    }
    if (-not $text) { Fail "nothing starting '$First' appeared" }
    Write-Output $text
  }
  'select' {
    $element = Until 30 { Named $First ([System.Windows.Automation.ControlType]::Text) }
    if (-not $element) { Fail "no text '$First' to select" }
    $item = $element
    while ($item -and $item.Current.ControlType -ne [System.Windows.Automation.ControlType]::ListItem) { $item = $Walker.GetParent($item) }
    if (-not $item) { Fail "'$First' is not inside a list item" }
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Write-Output "selected '$First'"
  }
  'invoke' {
    $button = Until 30 { Named $First ([System.Windows.Automation.ControlType]::Button) }
    if (-not $button) { Fail "no button '$First'" }
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Write-Output "invoked '$First'"
  }
  'set-after' {
    $label = Until 30 { Named $First ([System.Windows.Automation.ControlType]::Text) }
    if (-not $label) { Fail "no label '$First'" }
    $edit = $Walker.GetNextSibling($label)
    while ($edit -and $edit.Current.ControlType -ne [System.Windows.Automation.ControlType]::Edit) { $edit = $Walker.GetNextSibling($edit) }
    if (-not $edit) { Fail "no field after '$First'" }
    $value = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $value.SetValue($Second)
    if ($value.Current.Value -ne $Second) { Fail "the field after '$First' reads '$($value.Current.Value)'" }
    Write-Output "set the field after '$First'"
  }
  'set-only' {
    $edits = Until 30 {
      $window = Window
      if ($window) {
        $found = @($window.FindAll($Scope::Descendants,
          (New-Object System.Windows.Automation.PropertyCondition($Auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit))))
        if ($found.Count -gt 0) { return , $found }
      }
    }
    if (-not $edits -or $edits.Count -ne 1) { Fail "expected one field, found $(@($edits).Count)" }
    $value = $edits[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $value.SetValue($First)
    if ($value.Current.Value -ne $First) { Fail "the field reads '$($value.Current.Value)'" }
    Write-Output 'set the only field'
  }
  default { Fail "no action named $Action" }
}
