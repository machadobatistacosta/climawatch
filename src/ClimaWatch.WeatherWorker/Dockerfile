FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/ClimaWatch.WeatherWorker/ClimaWatch.WeatherWorker.csproj", "src/ClimaWatch.WeatherWorker/"]
COPY ["src/ClimaWatch.Contracts/ClimaWatch.Contracts.csproj", "src/ClimaWatch.Contracts/"]
COPY ["src/ClimaWatch.Infrastructure/ClimaWatch.Infrastructure.csproj", "src/ClimaWatch.Infrastructure/"]

RUN dotnet restore "src/ClimaWatch.WeatherWorker/ClimaWatch.WeatherWorker.csproj"

COPY . .
WORKDIR "/src/src/ClimaWatch.WeatherWorker"

RUN dotnet publish "ClimaWatch.WeatherWorker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "ClimaWatch.WeatherWorker.dll"]
