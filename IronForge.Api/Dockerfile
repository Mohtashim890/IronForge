# =========================
# Build stage
# =========================

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY ["IronForge.Api/IronForge.Api.csproj", "IronForge.Api/"]
COPY ["IronForge.Application/IronForge.Application.csproj", "IronForge.Application/"]
COPY ["IronForge.Infrastructure/IronForge.Infrastructure.csproj", "IronForge.Infrastructure/"]
COPY ["IronForge.Shared/IronForge.Shared.csproj", "IronForge.Shared/"]

RUN dotnet restore "IronForge.Api/IronForge.Api.csproj"

COPY . .

RUN dotnet publish "IronForge.Api/IronForge.Api.csproj" \
	-c Release \
	-o /app/publish \
	--no-restore

# =========================
# Runtime stage
# =========================

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

# Install curl for the Docker health check.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "IronForge.Api.dll"]

