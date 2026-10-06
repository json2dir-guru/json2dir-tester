# Builds the tester on Windows and runs it in WSL, where the implementations are tested.
# Usage: ./run.ps1 run json2dir     (arguments go to json2dir-tester as is)
# The WSL distribution is $env:J2D_WSL_DISTRO, Ubuntu by default.
$ErrorActionPreference = 'Stop'
$distro = if ($env:J2D_WSL_DISTRO) { $env:J2D_WSL_DISTRO } else { 'Ubuntu' }

dotnet build "$PSScriptRoot/src/Json2dirTester" -c Release --nologo -v quiet | Out-Host
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$dll = "$PSScriptRoot/src/Json2dirTester/bin/Release/net10.0/json2dir-tester.dll" -replace '\\', '/'
$wslDll = wsl -d $distro -- wslpath -a $dll
wsl -d $distro -- dotnet $wslDll @args
exit $LASTEXITCODE
