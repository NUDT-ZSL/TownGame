$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$downloads=Join-Path $taskRoot '.tools\downloads'
$dotnetRoot=Join-Path $taskRoot '.tools\dotnet'
$godotRoot=Join-Path $taskRoot '.tools\godot'
$godotExe=Join-Path $godotRoot 'Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
New-Item -ItemType Directory -Force -Path $downloads,$dotnetRoot,$godotRoot | Out-Null

if (!(Test-Path -LiteralPath $godotExe)) {
    $archive=Join-Path $downloads 'Godot_v4.7.2-stable_mono_win64.zip'
    Write-Output '正在下载 Godot 4.7.2 .NET 版…'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/godotengine/godot-builds/releases/download/4.7.2-stable/Godot_v4.7.2-stable_mono_win64.zip' -OutFile $archive
    Expand-Archive -LiteralPath $archive -DestinationPath $godotRoot -Force
    if (!(Test-Path -LiteralPath $godotExe)) {throw 'Godot 解压后未找到程序，请检查下载文件。'}
}

$dotnetExe=Join-Path $dotnetRoot 'dotnet.exe'
$hasSdk=(Test-Path -LiteralPath $dotnetExe) -and ((& $dotnetExe --list-sdks) -match '^8\.0\.')
if (!$hasSdk) {
    $installer=Join-Path $downloads 'dotnet-install.ps1'
    Write-Output '正在安装便携 .NET 8 SDK…'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & $installer -Channel '8.0' -Architecture 'x64' -InstallDir $dotnetRoot -NoPath
    if (!(Test-Path -LiteralPath $dotnetExe) -or !((& $dotnetExe --list-sdks) -match '^8\.0\.')) {throw '.NET 8 SDK 安装后未通过检查。'}
}

Write-Output '依赖已就绪。运行 .\native-godot\run.ps1 启动游戏。'
