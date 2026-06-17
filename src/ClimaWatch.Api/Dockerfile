FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/ClimaWatch.Api/ClimaWatch.Api.csproj", "src/ClimaWatch.Api/"]
COPY ["src/ClimaWatch.Contracts/ClimaWatch.Contracts.csproj", "src/ClimaWatch.Contracts/"]
COPY ["src/ClimaWatch.Infrastructure/ClimaWatch.Infrastructure.csproj", "src/ClimaWatch.Infrastructure/"]
RUN dotnet restore "src/ClimaWatch.Api/ClimaWatch.Api.csproj"

COPY . .
WORKDIR "/src/src/ClimaWatch.Api"
RUN dotnet publish "ClimaWatch.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ClimaWatch.Api.dll"]
