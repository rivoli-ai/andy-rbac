FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["Andy.Rbac.sln", "."]
COPY ["src/Andy.Rbac/Andy.Rbac.csproj", "src/Andy.Rbac/"]
COPY ["src/Andy.Rbac.Client/Andy.Rbac.Client.csproj", "src/Andy.Rbac.Client/"]
COPY ["src/Andy.Rbac.Web/Andy.Rbac.Web.csproj", "src/Andy.Rbac.Web/"]
COPY ["proto/rbac.proto", "proto/"]

# Restore
RUN dotnet restore "src/Andy.Rbac.Web/Andy.Rbac.Web.csproj"

# Copy source
COPY ["src/Andy.Rbac/", "src/Andy.Rbac/"]
COPY ["src/Andy.Rbac.Client/", "src/Andy.Rbac.Client/"]
COPY ["src/Andy.Rbac.Web/", "src/Andy.Rbac.Web/"]

# Build
WORKDIR "/src/src/Andy.Rbac.Web"
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Andy.Rbac.Web.dll"]
