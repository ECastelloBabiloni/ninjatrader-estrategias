param(
    [string]$NtBin = 'C:\Program Files\NinjaTrader 8\bin',
    [string]$NtCustom = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'NinjaTrader 8\bin\Custom')
)
$ErrorActionPreference = 'Stop'
$projectDir = Split-Path $PSScriptRoot -Parent
$buildDir = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $buildDir -Force | Out-Null
$fwDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.dll','System.Core.dll','System.Xml.dll','System.ComponentModel.DataAnnotations.dll') | ForEach-Object { Join-Path $fwDir $_ }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { Join-Path (Join-Path $fwDir 'WPF') $_ }
$references += @('NinjaTrader.Core.dll','NinjaTrader.Gui.dll','SharpDX.dll','SharpDX.Direct2D1.dll') | ForEach-Object { Join-Path $NtBin $_ }
$references += Join-Path $NtCustom 'NinjaTrader.Custom.dll'
$sources = @((Join-Path $NtCustom 'Strategies\@Strategy.cs'),(Join-Path $NtCustom 'Indicators\@Indicator.cs'),(Join-Path $NtCustom 'MarketAnalyzerColumns\@MarketAnalyzerColumn.cs'))
$sources += Get-ChildItem -LiteralPath (Join-Path $projectDir 'Strategies'),(Join-Path $projectDir 'Indicators'),(Join-Path $projectDir 'AddOns') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$arguments = @('/nologo','/target:library','/platform:x64',('/out:' + (Join-Path $buildDir 'Review.dll')))
$arguments += $references | ForEach-Object { '/reference:' + $_ }
& (Join-Path $fwDir 'csc.exe') @arguments @sources
if ($LASTEXITCODE -ne 0) { throw 'Compilación externa fallida.' }
'PASS: compilación externa con las DLL instaladas y los fuentes entregados. No es F5 ni ejecución de NinjaTrader.'
