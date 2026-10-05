
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY ["IronForge.Mcp.Client/IronForge.Mcp.Client.csproj", "IronForge.Mcp.Client/"]

RUN dotnet restore "IronForge.Mcp.Client/IronForge.Mcp.Client.csproj"

COPY . .

WORKDIR "/src/IronForge.Mcp.Client"

RUN dotnet publish "IronForge.Mcp.Client.csproj" \
   -c Release \
	-o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "IronForge.Mcp.Client.dll"]
