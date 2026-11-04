<#
.SYNOPSIS
  Copia as fotos do mouse do app oficial "ATTACK SHARK MOUSE HUB" já instalado no seu PC
  e as recorta para o SharkConfig (substitui as que já vêm no repositório). Útil se uma
  versão nova do app oficial trouxer fotos de modelos novos.

.EXAMPLE
  .\tools\extract-photos.ps1
  .\tools\extract-photos.ps1 -Source "D:\Apps\ATTACK  SHARK MOUSE HUB"
#>
param(
  [string]$Source,
  [string]$Dest = (Join-Path $PSScriptRoot "..\src\SharkConfig\wwwroot\img")
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

if (-not $Source) {
  $roots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}) | Where-Object { $_ }
  $Source = $roots | ForEach-Object { Get-ChildItem $_ -Directory -Filter "*SHARK*MOUSE HUB*" -ErrorAction SilentlyContinue } |
    Select-Object -First 1 -ExpandProperty FullName
  if (-not $Source) { throw "App oficial não encontrado. Instale o ATTACK SHARK MOUSE HUB ou informe -Source." }
}
$pics = Get-ChildItem $Source -Recurse -Filter "mouse*.png" | Where-Object { $_.Directory.Name -eq "Buttoninterface" }
if (-not $pics) { throw "Nenhuma foto em '$Source' (esperado ...\Picture\Buttoninterface\mouse*.png)." }

New-Item -ItemType Directory -Force $Dest | Out-Null
foreach ($f in $pics) {
  $src = [Drawing.Bitmap]::FromFile($f.FullName)
  try {
    # original 432×356 com margem transparente; o mouse ocupa x 115–317
    $dst = New-Object Drawing.Bitmap 202, 356, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($dst)
    $g.DrawImage($src, (New-Object Drawing.Rectangle 0, 0, 202, 356), (New-Object Drawing.Rectangle 115, 0, 202, 356), [Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    $dst.Save((Join-Path $Dest $f.Name), [Drawing.Imaging.ImageFormat]::Png)
    $dst.Dispose()
  } finally { $src.Dispose() }
}
"$($pics.Count) fotos copiadas para $((Resolve-Path $Dest).Path)"
