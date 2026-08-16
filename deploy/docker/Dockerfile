FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/PosServer ./src/PosServer

RUN dotnet restore src/PosServer/SolidPOS.PosServer.Api/SolidPOS.PosServer.Api.csproj
RUN dotnet publish src/PosServer/SolidPOS.PosServer.Api/SolidPOS.PosServer.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "SolidPOS.PosServer.Api.dll"]
