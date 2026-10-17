FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY ["IronForge.Platform/IronForge.Platform.csproj", "IronForge.Platform/"]
COPY ["IronForge.Shared/IronForge.Shared.csproj", "IronForge.Shared/"]
COPY ["IronForge.UI.Platform/IronForge.UI.Platform.csproj", "IronForge.UI.Platform/"]

RUN dotnet restore "IronForge.Platform/IronForge.Platform.csproj"

COPY . .

RUN dotnet publish "IronForge.Platform/IronForge.Platform.csproj" \
	-c Release \
	-o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "IronForge.Platform.dll"]
