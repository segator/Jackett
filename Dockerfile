FROM mcr.microsoft.com/dotnet/core/aspnet:3.1
WORKDIR /app
COPY src/Jackett.Server/bin/Release/netcoreapp3.1 /app/
ENTRYPOINT ["dotnet", "jackett.dll"]
