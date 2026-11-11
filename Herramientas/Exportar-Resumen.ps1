param(
    [Parameter(Mandatory=$true)][string]$Csv,
    [string]$Destino
)
$ErrorActionPreference = 'Stop'
$sourceFile = Get-Item -LiteralPath $Csv
if (-not $Destino) { $Destino = Join-Path $sourceFile.DirectoryName ($sourceFile.BaseName + '_analisis') }
New-Item -ItemType Directory -Path $Destino -Force | Out-Null
$firstRow = Import-Csv -LiteralPath $sourceFile.FullName | Select-Object -First 1
if ($null -eq $firstRow -or -not ($firstRow.PSObject.Properties.Name -contains 'event')) { throw 'CSV vacío o formato no reconocido.' }
# Separate streaming passes keep a multi-year tick file out of memory.
Import-Csv -LiteralPath $sourceFile.FullName | Where-Object event -eq 'TRADE' | Export-Csv -LiteralPath (Join-Path $Destino 'Operaciones.csv') -NoTypeInformation -Encoding UTF8
Import-Csv -LiteralPath $sourceFile.FullName | Where-Object { $_.event -in @('MARK','FILL','TRADE','BAR_RANGE') } | Export-Csv -LiteralPath (Join-Path $Destino 'Trayectoria.csv') -NoTypeInformation -Encoding UTF8
Import-Csv -LiteralPath $sourceFile.FullName | Where-Object event -ne 'MARK' | Export-Csv -LiteralPath (Join-Path $Destino 'Eventos.csv') -NoTypeInformation -Encoding UTF8
Write-Output "Exportación terminada: $Destino. Los END con INCOMPLETE_OPEN_POSITION requieren revisión."
