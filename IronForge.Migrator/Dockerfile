# =========================
# Build
# =========================

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY ["IronForge.Migrator/IronForge.Migrator.csproj", "IronForge.Migrator/"]
COPY ["IronForge.Infrastructure/IronForge.Infrastructure.csproj", "IronForge.Infrastructure/"]
COPY ["IronForge.Application/IronForge.Application.csproj", "IronForge.Application/"]
COPY ["IronForge.Shared/IronForge.Shared.csproj", "IronForge.Shared/"]

RUN dotnet restore "IronForge.Migrator/IronForge.Migrator.csproj"

COPY . .

RUN dotnet publish \
    "IronForge.Migrator/IronForge.Migrator.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false


# =========================
# Runtime - "runtime:10.0" it only needs the .NET runtime
# =========================

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "IronForge.Migrator.dll"]