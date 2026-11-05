param([ValidateSet('Unpack','Pack')][string]$Action='Unpack')
$ErrorActionPreference='Stop'
$contentDirectory=Join-Path $PSScriptRoot 'Content'
$plainFile=Join-Path $contentDirectory 'world.json'
$packedFile=Join-Path $contentDirectory 'world.json.gz'
# Assets are native project inputs. No legacy modules or Python interpreter are needed.
if ($Action -eq 'Unpack') {
    if (Test-Path -LiteralPath $plainFile) {throw 'world.json already exists. Keep your edits or rename it before unpacking.'}
    $contentSource=[IO.File]::OpenRead($packedFile)
    $contentTarget=[IO.File]::Create($plainFile)
    $contentStream=[IO.Compression.GZipStream]::new($contentSource,[IO.Compression.CompressionMode]::Decompress)
    try {$contentStream.CopyTo($contentTarget)} finally {$contentStream.Dispose();$contentSource.Dispose();$contentTarget.Dispose()}
} else {
    $null=ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($plainFile,[Text.Encoding]::UTF8))
    $contentSource=[IO.File]::OpenRead($plainFile)
    $contentTarget=[IO.File]::Create($packedFile+'.tmp')
    $contentStream=[IO.Compression.GZipStream]::new($contentTarget,[IO.Compression.CompressionLevel]::Optimal)
    try {$contentSource.CopyTo($contentStream)} finally {$contentStream.Dispose();$contentSource.Dispose();$contentTarget.Dispose()}
    Move-Item -LiteralPath ($packedFile+'.tmp') -Destination $packedFile -Force
}
Write-Host ('Native assets: '+$Action+' complete.')
