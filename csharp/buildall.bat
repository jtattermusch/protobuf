@rem Builds Google.Protobuf and runs the tests

dotnet --version

which dotnet

@rem dotnet restore -v diag src/Google.Protobuf.sln

dotnet build -v d src/Google.Protobuf.sln || goto :error

echo Running tests.

dotnet test src/Google.Protobuf.Test/Google.Protobuf.Test.csproj || goto :error

goto :EOF

:error
echo Failed!
exit /b %errorlevel%
