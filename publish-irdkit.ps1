dotnet publish -c Release -f net9.0 -r win-x86 --self-contained=false  -p:PublishSingleFile=true -p:DebugType=None IRDKit\IRDKit.csproj
dotnet publish -c Release -f net9.0 -r win-x64 --self-contained=false  -p:PublishSingleFile=true -p:DebugType=None IRDKit\IRDKit.csproj
dotnet publish -c Release -f net9.0 -r win-arm64 --self-contained=false  -p:PublishSingleFile=true -p:DebugType=None IRDKit\IRDKit.csproj
dotnet publish -c Release -f net9.0 -r linux-x64 --self-contained=false  -p:PublishSingleFile=true -p:DebugType=None IRDKit\IRDKit.csproj
dotnet publish -c Release -f net9.0 -r linux-arm64 --self-contained=false  -p:PublishSingleFile=true -p:DebugType=None IRDKit\IRDKit.csproj
dotnet publish -c Release -f net9.0 -r osx-x64 --self-contained=false  -p:PublishSingleFile=true -p:DebugType=None IRDKit\IRDKit.csproj
dotnet publish -c Release -f net9.0 -r osx-arm64 --self-contained=false  -p:PublishSingleFile=true -p:DebugType=None IRDKit\IRDKit.csproj

Compress-Archive -Path "./IRDKit/bin/Release/net9.0/win-x86/publish/irdkit.exe" -Destination "./irdkit-win-x86.zip" -CompressionLevel "Optimal"
Compress-Archive -Path "./IRDKit/bin/Release/net9.0/win-x64/publish/irdkit.exe" -Destination "./irdkit-win-x64.zip" -CompressionLevel "Optimal"
Compress-Archive -Path "./IRDKit/bin/Release/net9.0/win-arm64/publish/irdkit.exe" -Destination "./irdkit-win-arm64.zip" -CompressionLevel "Optimal"
Compress-Archive -Path "./IRDKit/bin/Release/net9.0/linux-x64/publish/irdkit" -Destination "./irdkit-linux-x64.zip" -CompressionLevel "Optimal"
Compress-Archive -Path "./IRDKit/bin/Release/net9.0/linux-arm64/publish/irdkit" -Destination "./irdkit-linux-arm64.zip" -CompressionLevel "Optimal"
Compress-Archive -Path "./IRDKit/bin/Release/net9.0/osx-x64/publish/irdkit" -Destination "./irdkit-osx-x64.zip" -CompressionLevel "Optimal"
Compress-Archive -Path "./IRDKit/bin/Release/net9.0/osx-arm64/publish/irdkit" -Destination "./irdkit-osx-arm64.zip" -CompressionLevel "Optimal"