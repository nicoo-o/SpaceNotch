# Les deux variantes de SpaceNotch.exe (n° 51), pour la CI et la publication.
#
#   - « téléchargée » : installeur et portable, identité de paquet éteinte ;
#   - « installée » : la même, identité allumée. Elle n'est pas publiée : seule
#     sa signature l'est (SpaceNotch-identity.bin, quelques Ko), avec son
#     empreinte dans SHA256SUMS.txt (« SpaceNotch-installed.exe »).
#
# L'installeur refait l'installée octet pour octet à partir de la téléchargée
# (InstalledVariant, côté application) : il ne casse plus aucune signature.
# Le format de SpaceNotch-identity.bin est celui d'InstalledVariant.Serialize.

# Allume ou éteint l'élément <msix> du manifeste, sans changer la longueur du fichier.
function Set-IdentityManifest([string] $Path, [bool] $On) {
  $bytes = [IO.File]::ReadAllBytes($Path)
  $latin = [Text.Encoding]::GetEncoding(28591)
  $text = $latin.GetString($bytes)
  $found = [regex]::Matches($text, '<msix [^<>]*urn:schemas-microsoft-com:msix\.v1[^<>]*></msix>|<!--x [^<>]*urn:schemas-microsoft-com:msix\.v1[^<>]*></ms-->')
  if ($found.Count -ne 1) { throw "$Path : $($found.Count) élément(s) d'identité, 1 attendu." }
  $m = $found[0]
  $inner = $m.Value.Substring(6, $m.Length - 14)
  $value = if ($On) { "<msix $inner></msix>" } else { "<!--x $inner></ms-->" }
  if ($value.Length -ne $m.Length) { throw "Longueur modifiée : $Path" }
  $latin.GetBytes($value).CopyTo($bytes, $m.Index)
  [IO.File]::WriteAllBytes($Path, $bytes)
  Write-Host ("{0} : identité {1} (octet {2})" -f [IO.Path]::GetFileName($Path), $(if ($On) { "allumée" } else { "éteinte" }), $m.Index)
}

# Écrit la signature de la variante installée : somme de contrôle de l'en-tête PE,
# puis la table des certificats (vide si l'exécutable n'est pas signé).
function Export-VariantSignature([string] $Installed, [string] $Out) {
  $bytes = [IO.File]::ReadAllBytes($Installed)
  $pe = [BitConverter]::ToInt32($bytes, 0x3C)
  $optional = $pe + 24
  $directories = switch ([BitConverter]::ToUInt16($bytes, $optional)) {
    0x20B { $optional + 112 }
    0x10B { $optional + 96 }
    default { throw "$Installed : en-tête PE inconnu." }
  }
  $security = $directories + 32
  $checksum = [BitConverter]::ToUInt32($bytes, $optional + 64)
  $offset = [BitConverter]::ToUInt32($bytes, $security)
  $size = [BitConverter]::ToUInt32($bytes, $security + 4)

  if ($size -gt 0 -and ([long]$offset + $size) -ne $bytes.Length) { throw "$Installed : la table des certificats n'est pas à la fin." }
  if ($size -eq 0) { $offset = 0 }

  $stream = [IO.File]::Create($Out)
  try {
    $writer = [IO.BinaryWriter]::new($stream)
    $writer.Write([Text.Encoding]::ASCII.GetBytes("SNSIG1`0`0"))
    $writer.Write([uint32] $checksum)
    $writer.Write([uint32] $offset)
    $writer.Write([uint32] $size)
    if ($size -gt 0) { $writer.Write($bytes, [int] $offset, [int] $size) }
    $writer.Flush()
  } finally {
    $stream.Dispose()
  }
  Write-Host ("{0} : signature de la variante installée, {1} octets de certificats" -f [IO.Path]::GetFileName($Out), $size)
}
