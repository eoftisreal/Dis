param([string]$Executable = 'artifacts/windows/Dis.exe')
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Executable).Path
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class DisLifecycleWindow {
    private delegate bool Callback(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    public static IntPtr Find(uint process) {
        IntPtr result = IntPtr.Zero;
        EnumWindows((window, state) => {
            uint owner; GetWindowThreadProcessId(window, out owner);
            var title = new StringBuilder(256); GetWindowText(window, title, 256);
            if (owner == process && title.ToString().StartsWith("Dis")) { result = window; return false; }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
'@
$originalEngines = @(Get-Process -Name Dis.Engine -ErrorAction SilentlyContinue | ForEach-Object Id)
$process = Start-Process -FilePath $exe -ArgumentList '--no-auto-connect' -WindowStyle Hidden -PassThru
try {
    Start-Sleep -Seconds 3
    $process.Refresh()
    if ($process.HasExited) { throw 'A desktop instance may already be running; close it before running this check.' }
    $window = [DisLifecycleWindow]::Find($process.Id)
    if ($window -eq [IntPtr]::Zero) { throw 'Desktop window was unavailable.' }
    if (-not [DisLifecycleWindow]::PostMessage($window, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)) { throw 'Could not close the desktop window.' }
    Start-Sleep -Seconds 1
    if ($process.HasExited) { throw 'Closing the window exited Dis. Enable Keep Dis running in the tray before testing.' }
    if ([DisLifecycleWindow]::IsWindowVisible($window)) { throw 'Closing the window did not hide it.' }
    $reopen = Start-Process -FilePath $exe -ArgumentList '--no-auto-connect' -WindowStyle Hidden -PassThru
    if (-not $reopen.WaitForExit(5000)) { throw 'The second invocation did not hand off to the existing instance.' }
    Start-Sleep -Seconds 1
    $process.Refresh()
    if (-not [DisLifecycleWindow]::IsWindowVisible($window)) { throw 'Reopening Dis did not restore the window.' }
    $quit = Start-Process -FilePath $exe -ArgumentList '--quit' -WindowStyle Hidden -PassThru
    if (-not $quit.WaitForExit(5000) -or -not $process.WaitForExit(15000)) { throw 'Quit did not shut down the desktop.' }
    $leftover = @(Get-Process -Name Dis.Engine -ErrorAction SilentlyContinue | Where-Object { $_.Id -notin $originalEngines })
    if ($leftover.Count) { throw 'The engine remained running after Quit.' }
    @{ closeToTray = $true; reopenSameInstance = $true; gracefulQuit = $true; noOrphanEngine = $true } |
        ConvertTo-Json | Set-Content -LiteralPath artifacts/desktop-lifecycle.json
    Write-Host 'PASS close to tray, single-instance reopen, graceful quit, and engine cleanup.'
} finally {
    if (-not $process.HasExited) {
        Start-Process -FilePath $exe -ArgumentList '--quit' -WindowStyle Hidden -Wait
        $process.WaitForExit(15000) | Out-Null
    }
}
