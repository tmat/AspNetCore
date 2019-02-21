set target=%1
set sdkVersion=%2
set runtimeVersion=%3
set DOTNET_ROOT=%HELIX_CORRELATION_PAYLOAD%/sdk
powershell.exe -NoProfile -ExecutionPolicy unrestricted -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; &([scriptblock]::Create((Invoke-WebRequest -useb 'https://dot.net/v1/dotnet-install.ps1'))) -Version %sdkVersion% -InstallDir %DOTNET_ROOT%"
powershell.exe -NoProfile -ExecutionPolicy unrestricted -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; &([scriptblock]::Create((Invoke-WebRequest -useb 'https://dot.net/v1/dotnet-install.ps1'))) -Runtime dotnet -Version %runtimeVersion% -InstallDir %DOTNET_ROOT%"
set DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
set PATH=%DOTNET_ROOT%;%PATH%
set DOTNET_MULTILEVEL_LOOKUP=0
set DOTNET_CLI_HOME=%DOTNET_ROOT%
set helix=true
%DOTNET_ROOT%\dotnet vstest %target% --logger:trx


