$ErrorActionPreference = 'Stop'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw "Nie znaleziono kompilatora: $compiler"
}

$outputDirectory = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File | Select-Object -ExpandProperty FullName
$outputPath = Join-Path $outputDirectory 'MaskOver.exe'

& $compiler /nologo /target:winexe /platform:x64 /unsafe /optimize+ `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    "/out:$outputPath" $sources

if ($LASTEXITCODE -ne 0) {
    throw "Kompilacja nie powiodla sie. Kod: $LASTEXITCODE"
}

Write-Host "Gotowe: $outputPath"
