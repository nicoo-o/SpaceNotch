# Sonde (ADR-023) : un paquet d'identité signé par un certificat éphémère — créé
# pour l'occasion, clé privée jetée après signature, certificat public approuvé
# pour l'utilisateur seulement — donne-t-il une identité à un exécutable, et
# l'écoute des notifications est-elle alors possible ?
$ErrorActionPreference = "Stop"
$publisher = "CN=SpaceNotch"
$root = $env:RUNNER_TEMP
$app = Join-Path $root "probe-app"
$pkg = Join-Path $root "probe-pkg"

# 1. L'exécutable, avec l'élément <msix>.
(Get-Content tools/identity-probe/app.manifest.template -Raw).Replace("__PUBLISHER__", $publisher) | Set-Content tools/identity-probe/app.manifest -Encoding utf8
dotnet publish tools/identity-probe/Probe.csproj -c Release -r win-x64 --self-contained false -o $app -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Publication de la sonde impossible." }
Write-Host "--- sans paquet ---"
& "$app\Probe.exe"

# 2. Le paquet.
New-Item -ItemType Directory -Force "$pkg\Assets" | Out-Null
Copy-Item packaging/identity/Assets/* "$pkg\Assets"
(Get-Content tools/identity-probe/AppxManifest.template.xml -Raw).Replace("__PUBLISHER__", $publisher) | Set-Content "$pkg\AppxManifest.xml" -Encoding utf8
$sdk = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64" -Directory | Sort-Object FullName -Descending | Select-Object -First 1
& "$($sdk.FullName)\makeappx.exe" pack /d $pkg /p "$pkg.msix" /nv /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "MakeAppx a échoué." }

# 3. Certificat éphémère : créé, utilisé, exporté en public, clé privée détruite.
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $publisher -KeyUsage DigitalSignature `
  -FriendlyName "SpaceNotch (paquet d'identité)" -CertStoreLocation Cert:\CurrentUser\My `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
$password = ConvertTo-SecureString -String ([guid]::NewGuid().ToString()) -Force -AsPlainText
$pfx = Join-Path $root "probe.pfx"
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $password | Out-Null
$plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($password))
& "$($sdk.FullName)\signtool.exe" sign /fd SHA256 /f $pfx /p $plain "$pkg.msix"
if ($LASTEXITCODE -ne 0) { throw "Signature impossible." }
$cer = Join-Path $root "probe.cer"
Export-Certificate -Cert $cert -FilePath $cer | Out-Null
Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)" -DeleteKey
Remove-Item $pfx
Write-Host "Certificat $($cert.Thumbprint) : clé privée détruite, seul le public reste."

# 4. Confiance pour l'utilisateur seulement (sans administrateur), puis enregistrement.
Import-Certificate -FilePath $cer -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null
try {
  Add-AppxPackage -Path "$pkg.msix" -ExternalLocation $app
  Write-Host "ENREGISTREMENT (confiance utilisateur): réussi"
} catch {
  Write-Host "ENREGISTREMENT (confiance utilisateur): refusé — $($_.Exception.Message)"
  Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
  Add-AppxPackage -Path "$pkg.msix" -ExternalLocation $app
  Write-Host "ENREGISTREMENT (confiance machine): réussi"
}

Write-Host "--- avec paquet ---"
& "$app\Probe.exe"

Get-AppxPackage -Name SpaceNotch.Probe | Remove-AppxPackage
Write-Host "Paquet retiré."
