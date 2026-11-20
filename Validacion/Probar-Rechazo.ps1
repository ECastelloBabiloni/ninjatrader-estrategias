$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$build=Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $build -Force | Out-Null
$exe=Join-Path $build 'RechazoValidation.exe'
$sources=@('AddOns\CalendarioSesionesNY.cs','Indicators\NivelesImportantesNY.cs','Strategies\ExperimentoAMath.cs','Strategies\RechazoNivelesCore.cs','Validacion\RechazoValidation.cs') | ForEach-Object {Join-Path $repo $_}
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:exe /define:NIVELES_CORE_ONLY ('/out:'+$exe) @sources
if($LASTEXITCODE -ne 0) {throw 'Error de compilación de pruebas de rechazo.'}
& $exe | Tee-Object -FilePath (Join-Path $PSScriptRoot 'ResultadoRechazo.txt')
if($LASTEXITCODE -ne 0) {throw 'Fallaron las pruebas de rechazo.'}
