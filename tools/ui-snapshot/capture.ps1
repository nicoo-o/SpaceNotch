# Capture le haut de l'ecran principal, la ou vivent la notch et l'installeur :
# plusieurs images espacees, pour voir aussi l'animation d'ouverture.
#
# Le processus se declare DPI-aware avant toute mesure (n 31) : sans cela,
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
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
}
"@
[void][SnapshotDpi]::SetProcessDPIAware()

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$scale = [SnapshotDpi]::GetDpiForSystem() / 96.0
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
