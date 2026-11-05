param([switch]$SkipZip)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$sdkRoot=Join-Path $projectRoot '.tools\dotnet'
$engineRoot=Join-Path $projectRoot '.tools\godot\Godot_v4.7.2-stable_mono_win64'
$env:DOTNET_ROOT=$sdkRoot
$env:DOTNET_CLI_HOME=Join-Path $projectRoot '.tools\dotnet-home'
$env:NUGET_PACKAGES=Join-Path $projectRoot '.tools\nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
& (Join-Path $sdkRoot 'dotnet.exe') build (Join-Path $PSScriptRoot 'TownNative.csproj') --configfile (Join-Path $PSScriptRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) {throw 'Native build failed.'}
$packageRoot=Join-Path $PSScriptRoot ('dist\TownGame-Windows-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $packageRoot | Out-Null
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'dist\.gdignore'),'')
foreach ($name in @('project.godot','Main.tscn','Scripts','Content')) {Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $packageRoot -Recurse}
# The compressed, versioned assets are authoritative in a release.
$packagePlain=Join-Path $packageRoot 'Content\world.json'
if (Test-Path -LiteralPath $packagePlain) {Remove-Item -LiteralPath $packagePlain}
$assemblyTarget=Join-Path $packageRoot '.godot\mono\temp\bin'
New-Item -ItemType Directory -Path $assemblyTarget -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '.godot\mono\temp\bin\Debug') -Destination $assemblyTarget -Recurse
$runtimeRoot=Join-Path $packageRoot 'Runtime'
New-Item -ItemType Directory -Path $runtimeRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $sdkRoot 'dotnet.exe') -Destination $runtimeRoot
Copy-Item -LiteralPath (Join-Path $sdkRoot 'host') -Destination $runtimeRoot -Recurse
New-Item -ItemType Directory -Path (Join-Path $runtimeRoot 'shared') | Out-Null
Copy-Item -LiteralPath (Join-Path $sdkRoot 'shared\Microsoft.NETCore.App') -Destination (Join-Path $runtimeRoot 'shared') -Recurse
foreach($name in @('LICENSE.txt','ThirdPartyNotices.txt')) {if(Test-Path -LiteralPath (Join-Path $sdkRoot $name)){Copy-Item -LiteralPath (Join-Path $sdkRoot $name) -Destination $runtimeRoot}}
New-Item -ItemType Directory -Path (Join-Path $packageRoot 'Engine') | Out-Null
foreach($name in @('Godot_v4.7.2-stable_mono_win64.exe','Godot_v4.7.2-stable_mono_win64_console.exe','GodotSharp')){Copy-Item -LiteralPath (Join-Path $engineRoot $name) -Destination (Join-Path $packageRoot 'Engine') -Recurse}
$startText='@echo off'+"`r`n"+'set "DOTNET_ROOT=%~dp0Runtime"'+"`r`n"+'set "PATH=%DOTNET_ROOT%;%PATH%"'+"`r`n"+'"%~dp0Engine\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0." %*'+"`r`n"
[IO.File]::WriteAllText((Join-Path $packageRoot 'Start-Town.cmd'),$startText,[Text.Encoding]::ASCII)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PORTABLE-README.txt') -Destination (Join-Path $packageRoot 'README.txt')
& (Join-Path $engineRoot 'Godot_v4.7.2-stable_mono_win64_console.exe') --headless --log-file (Join-Path $packageRoot 'Engine\license-generation.log') --path $PSScriptRoot -- ('--write-engine-notices='+(Join-Path $packageRoot 'Engine\GODOT-LICENSE.txt'))
if ($LASTEXITCODE -ne 0) {throw 'Unable to copy Godot license.'}
Write-Output ('PACKAGE_DIRECTORY='+$packageRoot)
if (!$SkipZip) {Compress-Archive -LiteralPath $packageRoot -DestinationPath ($packageRoot+'.zip');Write-Output ('PACKAGE_ZIP='+$packageRoot+'.zip')}
