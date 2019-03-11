@rem enter repo root
cd /d %~dp0\..\..\..\..

cd csharp

powershell -File get-dotnet.ps1

call build_packages.bat
