param([Parameter(Mandatory=$true)][string]$EventsPath,[string]$OutputPath)
$ErrorActionPreference='Stop'
$inputFile=(Resolve-Path -LiteralPath $EventsPath).Path
if(-not $OutputPath) {$OutputPath=Join-Path (Split-Path $inputFile -Parent) (([IO.Path]::GetFileNameWithoutExtension($inputFile))+'_summary.csv')}
if([IO.Path]::GetFullPath($OutputPath) -eq $inputFile) {throw 'El resumen no puede sustituir el archivo de eventos.'}
$rows=Import-Csv -LiteralPath $inputFile
$culture=[Globalization.CultureInfo]::InvariantCulture
$summary=@($rows | Where-Object {$_.event -eq 'POSITION_RESULT'} | ForEach-Object {
    $row=$_
    $fills=@($rows | Where-Object {$_.position -eq $row.position -and $_.event -eq 'FILL_ENTRY'})
    $gross=[double]::Parse($row.realised_gross,$culture)
    $fee=[double]::Parse($row.fee_reserved,$culture)
    [pscustomobject]@{run=$row.run;version=$row.version;position=$row.position;instrument=$row.instrument;first_fill_ny=($fills | Select-Object -First 1).time_ny;exit_ny=$row.time_ny;exit_reason=$row.reason;contracts_entered=$row.bought;gross=$gross;fee=$fee;net=($gross-$fee);origin_band=$row.origin_band;origin_mas=$row.origin_mas;recovery=(@($fills | Where-Object {$_.reason -eq 'RN_E2'}).Count -gt 0)}
})
$summary | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Encoding UTF8
Write-Output ('Resumen regenerado: '+$OutputPath+' | posiciones cerradas: '+$summary.Count)
