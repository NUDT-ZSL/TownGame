param([switch]$Editor,[switch]$Smoke,[switch]$Benchmark,[switch]$BenchmarkSimulation,[switch]$Verify,[switch]$UiSmoke,[switch]$ResidenceSmoke,[switch]$ResidencePreview,[switch]$FarmSmoke,[switch]$FarmPreview,[switch]$FreightSmoke,[switch]$FreightPreview)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskDotnet=Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
$taskGodot=Join-Path $taskRoot '.tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
if (!(Test-Path $taskDotnet) -or !(Test-Path $taskGodot)) {throw '缺少便携Godot/.NET工具，请参阅README。'}
$env:DOTNET_ROOT=Split-Path $taskDotnet
$env:PATH="$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_HOME=Join-Path $taskRoot '.tools\dotnet-home'
$env:NUGET_PACKAGES=Join-Path $taskRoot '.tools\nuget'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
if (!(Test-Path (Join-Path $PSScriptRoot 'Content\world.json'))) {throw '请先使用Pet环境执行 python tools/export_godot.py 生成场景。'}
& $taskDotnet build (Join-Path $PSScriptRoot 'TownNative.csproj') --configfile (Join-Path $PSScriptRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) {throw 'C#编译失败'}
$taskArgs=@('--path',$PSScriptRoot)
if ($Editor) {$taskArgs+='--editor'}
if ($Smoke) {$taskArgs+=@('--headless','--','--smoke')}
if ($Verify) {$taskArgs+=@('--headless','--','--verify-rules')}
if ($UiSmoke) {$taskArgs+=@('--headless','--','--ui-smoke')}
if ($Benchmark) {$taskArgs+=@('--','--benchmark')}
if ($BenchmarkSimulation) {$taskArgs+=@('--','--benchmark-simulation')}
if ($ResidenceSmoke) {$taskArgs+=@('--headless','--','--verify-residences')}
if ($ResidencePreview) {$taskArgs+=@('--','--verify-residences')}
if ($FarmSmoke) {$taskArgs+=@('--headless','--','--verify-farm-manager')}
if ($FarmPreview) {$taskArgs+=@('--','--verify-farm-manager')}
if ($FreightSmoke) {$taskArgs+=@('--headless','--','--verify-freight')}
if ($FreightPreview) {$taskArgs+=@('--','--verify-freight')}
& $taskGodot @taskArgs
exit $LASTEXITCODE
