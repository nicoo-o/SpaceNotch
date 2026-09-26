# Sonde : quels éditeurs l'élément <msix> accepte-t-il, et un paquet non signé
# donne-t-il vraiment une identité à un exécutable ? Voir ADR-023.
$ErrorActionPreference = "Continue"
$oid = "OID.2.25.311729368913984317654407730594956997722=1"
$variants = [ordered]@{
  "virgule-espace" = "CN=SpaceNotch, $oid"
  "virgule"        = "CN=SpaceNotch,$oid"
  "oid-seul"       = $oid
  "cn-seul"        = "CN=SpaceNotch"
}

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class ActCtx {
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct ACTCTX { public int cbSize; public uint dwFlags; public string lpSource; public ushort wProcessorArchitecture; public ushort wLangId; public string lpAssemblyDirectory; public string lpResourceName; public string lpApplicationName; public IntPtr hModule; }
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr CreateActCtxW(ref ACTCTX ctx);
  [DllImport("kernel32.dll")] public static extern void ReleaseActCtx(IntPtr h);
  public static int Test(string path) {
    var c = new ACTCTX(); c.cbSize = Marshal.SizeOf(typeof(ACTCTX)); c.lpSource = path;
    IntPtr h = CreateActCtxW(ref c);
    if (h == new IntPtr(-1)) return Marshal.GetLastWin32Error();
    ReleaseActCtx(h); return 0;
  }
}
"@

$accepted = @()
foreach ($name in $variants.Keys) {
  $pub = $variants[$name]
  $file = Join-Path $env:RUNNER_TEMP "probe-$name.manifest"
  @"
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="Probe"/>
  <msix xmlns="urn:schemas-microsoft-com:msix.v1" publisher="$pub" packageName="SpaceNotch.Probe" applicationId="Probe"/>
</assembly>
"@ | Set-Content $file -Encoding utf8
  $code = [ActCtx]::Test($file)
  Write-Host ("MANIFESTE {0,-15} [{1}] -> {2}" -f $name, $pub, $(if ($code -eq 0) { "ACCEPTÉ" } else { "REFUSÉ ($code)" }))
  if ($code -eq 0) { $accepted += $name }
}

# Essai de bout en bout avec l'éditeur non signé, s'il passe le manifeste.
foreach ($name in @("virgule-espace", "virgule")) {
  if ($accepted -notcontains $name) { continue }
  $pub = $variants[$name]
  Write-Host "===== Essai complet : $name ====="
  $dir = Join-Path $env:RUNNER_TEMP "probe-app-$name"
  Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
  New-Item -ItemType Directory -Force $dir | Out-Null
  (Get-Content tools/identity-probe/app.manifest.template -Raw).Replace("__PUBLISHER__", $pub) | Set-Content tools/identity-probe/app.manifest -Encoding utf8
  dotnet publish tools/identity-probe/Probe.csproj -c Release -r win-x64 --self-contained false -o $dir -nologo -v q | Out-Null
  & "$dir\Probe.exe"

  $pkg = Join-Path $env:RUNNER_TEMP "probe-pkg-$name"
  New-Item -ItemType Directory -Force "$pkg\Assets" | Out-Null
  Copy-Item packaging/identity/Assets/* "$pkg\Assets"
  (Get-Content tools/identity-probe/AppxManifest.template.xml -Raw).Replace("__PUBLISHER__", $pub) | Set-Content "$pkg\AppxManifest.xml" -Encoding utf8
  $makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" | Sort-Object FullName -Descending | Select-Object -First 1
  & $makeappx.FullName pack /d $pkg /p "$pkg.msix" /nv /o | Out-Null
  try {
    Add-AppxPackage -Path "$pkg.msix" -ExternalLocation $dir -AllowUnsigned -ErrorAction Stop
    Write-Host "ENREGISTREMENT: réussi"
    & "$dir\Probe.exe"
    Get-AppxPackage -Name SpaceNotch.Probe | Remove-AppxPackage
  } catch {
    Write-Host "ENREGISTREMENT: refusé — $($_.Exception.Message)"
  }
}
