@rem Builds Google.Protobuf and runs the tests

dotnet --version

which dotnet

dotnet restore -v diag src/Google.Protobuf.sln

dotnet build -v diag src/Google.Protobuf.sln || goto :error

echo Running tests.

dotnet test src/Google.Protobuf.Test/Google.Protobuf.Test.csproj || goto :error

goto :EOF

:error
echo Failed!
exit /b %errorlevel%
