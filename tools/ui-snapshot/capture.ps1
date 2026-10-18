# Capture le haut de l'ecran principal, la ou vivent la notch et l'installeur :
# plusieurs images espacees, pour voir aussi l'animation d'ouverture.
#
# Le processus se declare DPI-aware par ecran (PerMonitorV2) avant toute mesure (n 31) : sans cela,
# PowerShell 5.1 a 150 % voyait un ecran de 1707 px de large et ne capturait
# que le coin haut gauche de l'ecran reel. Les images sont en pixels physiques ;
# -Height reste en DIP (520 par defaut), converti a l'echelle de l'ecran.
# Pas d'accents dans ce script : PowerShell 5.1 lit un fichier sans BOM en ANSI.
param(
    [Parameter(Mandatory)] [string] $OutDir,
    [Parameter(Mandatory)] [string] $Name,
    [int] $Count = 4,
    [int] $IntervalMs = 700,
    [int] $Height = 520
)

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class SnapshotDpi {
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}
"@
# -4 : DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 ; repli sur le DPI systeme.
if (-not [SnapshotDpi]::SetProcessDpiAwarenessContext([IntPtr](-4))) { [void][SnapshotDpi]::SetProcessDPIAware() }

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
# Echelle de l'ecran principal (celui qui contient 0,0), lue a l'instant.
$origin = New-Object SnapshotDpi+POINT
$monitor = [SnapshotDpi]::MonitorFromPoint($origin, 1)
$dpiX = [uint32]96; $dpiY = [uint32]96
if ([SnapshotDpi]::GetDpiForMonitor($monitor, 0, [ref]$dpiX, [ref]$dpiY) -ne 0) { $dpiX = 96 }
$scale = $dpiX / 96.0
$h = [Math]::Min([int][Math]::Ceiling($Height * $scale), $screen.Height)
Write-Host ("Ecran principal : {0} x {1} px, echelle {2:P0}" -f $screen.Width, $screen.Height, $scale)

for ($i = 1; $i -le $Count; $i++) {
    $bitmap = New-Object System.Drawing.Bitmap $screen.Width, $h
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($screen.X, $screen.Y, 0, 0, $bitmap.Size)
    $path = Join-Path $OutDir ("{0}-{1}.png" -f $Name, $i)
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
    Write-Host "Capture : $path"
    Start-Sleep -Milliseconds $IntervalMs
}
