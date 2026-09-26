$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot 'bin\MaskOver.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    & (Join-Path $PSScriptRoot 'build.ps1')
}
Start-Process -FilePath $executable -WorkingDirectory (Split-Path -Parent $executable)
