param([string]$ServerUrl = 'https://zippy-api.onrender.com', [string]$BackendPath = '')
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskDist = Join-Path $taskRoot 'dist'
New-Item -ItemType Directory -Force -Path $taskDist | Out-Null
dotnet publish (Join-Path $taskRoot 'ZippyPrintAgent.csproj') -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' "-p:AgentServerUrl=$ServerUrl" -o (Join-Path $taskDist 'agent')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o agente.' }
Compress-Archive -Path (Join-Path $taskDist 'agent/*') -DestinationPath (Join-Path $taskDist 'payload.zip') -Force
dotnet publish (Join-Path $taskRoot 'Setup/ZippyPrintSetup.csproj') -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' -o (Join-Path $taskDist 'setup')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o instalador.' }
if ($BackendPath) {
 $taskTarget = Join-Path ([IO.Path]::GetFullPath($BackendPath)) 'PrintAgent'
 New-Item -ItemType Directory -Force -Path $taskTarget | Out-Null
 Copy-Item -LiteralPath (Join-Path $taskDist 'setup/ZippyPrintSetup.exe') -Destination (Join-Path $taskTarget 'ZippyPrintSetup.exe') -Force
}
Get-Item -LiteralPath (Join-Path $taskDist 'setup/ZippyPrintSetup.exe') | Select-Object FullName,Length
