FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY ["IronForge.Web/IronForge.Web.csproj", "IronForge.Web/"]
COPY ["IronForge.Shared/IronForge.Shared.csproj", "IronForge.Shared/"]

RUN dotnet restore "IronForge.Web/IronForge.Web.csproj"

COPY . .

RUN dotnet publish "IronForge.Web/IronForge.Web.csproj" \
	-c Release \
	-o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "IronForge.Web.dll"]
