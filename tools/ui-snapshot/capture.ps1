# Capture le haut de l'écran principal, là où vivent la notch et l'installeur :
# plusieurs images espacées, pour voir aussi l'animation d'ouverture.
param(
    [Parameter(Mandatory)] [string] $OutDir,
    [Parameter(Mandatory)] [string] $Name,
    [int] $Count = 4,
    [int] $IntervalMs = 700,
    [int] $Height = 520
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$h = [Math]::Min($Height, $screen.Height)

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
