# TouchpadGuard self-check: verifies that pointer movement is ignored right after a key press,
# and works again once you stop typing.
$ErrorActionPreference = 'Stop'
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class GuardTest {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    public struct POINT { public int X; public int Y; }
    public const uint MoveFlag = 0x0001;
    public static void TapF24() { keybd_event(0x87, 0, 0, IntPtr.Zero); keybd_event(0x87, 0, 2, IntPtr.Zero); }
    public static void MoveBy(int dx, int dy) { mouse_event(MoveFlag, dx, dy, 0, IntPtr.Zero); }
    public static string Pos() { POINT p; GetCursorPos(out p); return p.X + "," + p.Y; }
}
"@

if (-not (Get-Process TouchpadGuard -ErrorAction SilentlyContinue)) {
    throw 'TouchpadGuard is not running. Start TouchpadGuard.exe first.'
}

[GuardTest]::SetCursorPos(700, 500) | Out-Null
Start-Sleep -Milliseconds 200
$start = [GuardTest]::Pos()

[GuardTest]::TapF24()
[GuardTest]::MoveBy(60, 30)
Start-Sleep -Milliseconds 80
$typing = [GuardTest]::Pos()

Start-Sleep -Milliseconds 1200
[GuardTest]::MoveBy(60, 30)
Start-Sleep -Milliseconds 80
$idle = [GuardTest]::Pos()

[GuardTest]::SetCursorPos(700, 500) | Out-Null

"start : $start"
"typing: $typing"
"idle  : $idle"
"blocked while typing : $($start -eq $typing)"
"moves again when idle: $($typing -ne $idle)"
