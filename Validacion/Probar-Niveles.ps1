$ErrorActionPreference = 'Stop'
$projectDir = Split-Path $PSScriptRoot -Parent
$buildDir = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $buildDir -Force | Out-Null
$exe = Join-Path $buildDir 'NivelesValidation.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /define:NIVELES_CORE_ONLY ("/out:" + $exe) (Join-Path $projectDir 'AddOns\CalendarioSesionesNY.cs') (Join-Path $projectDir 'Indicators\NivelesImportantesNY.cs') (Join-Path $PSScriptRoot 'NivelesValidation.cs')
if ($LASTEXITCODE -ne 0) { throw 'Fallo al compilar pruebas de niveles.' }
& $exe | Tee-Object -FilePath (Join-Path $PSScriptRoot 'ResultadoNiveles.txt')
if ($LASTEXITCODE -ne 0) { throw 'Pruebas de niveles fallidas.' }
