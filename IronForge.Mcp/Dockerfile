FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /mcp-src

COPY ["IronForge.Mcp/IronForge.Mcp.csproj", "IronForge.Mcp/"]
COPY ["IronForge.Application/IronForge.Application.csproj", "IronForge.Application/"]
COPY ["IronForge.Infrastructure/IronForge.Infrastructure.csproj", "IronForge.Infrastructure/"]
COPY ["IronForge.Shared/IronForge.Shared.csproj", "IronForge.Shared/"]

RUN dotnet restore "IronForge.Mcp/IronForge.Mcp.csproj"

COPY . .

RUN dotnet publish "IronForge.Mcp/IronForge.Mcp.csproj" \
	-c Release \
	-o /mcp-app/publish \
	--no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /mcp-app

# Install curl for the Docker health check.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

EXPOSE 8080

COPY --from=build /mcp-app/publish .

ENTRYPOINT ["dotnet", "IronForge.Mcp.dll"]
