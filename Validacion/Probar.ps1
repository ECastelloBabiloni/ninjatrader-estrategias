$ErrorActionPreference = 'Stop'
$projectDir = Split-Path $PSScriptRoot -Parent
$exePath = Join-Path $PSScriptRoot 'Validation.exe'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe ("/out:" + $exePath) (Join-Path $projectDir 'Strategies\ExperimentoAMath.cs') (Join-Path $PSScriptRoot 'Validation.cs')
if ($LASTEXITCODE -ne 0) { throw 'Fallo al compilar pruebas.' }
& $exePath | Tee-Object -FilePath (Join-Path $PSScriptRoot 'Resultado.txt')
if ($LASTEXITCODE -ne 0) { throw 'Pruebas fallidas.' }
