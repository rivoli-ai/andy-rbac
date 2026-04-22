FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /build

# Trust corporate CAs
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates openssl && rm -rf /var/lib/apt/lists/*
COPY --from=certs . /usr/local/share/ca-certificates/corporate/
RUN find /usr/local/share/ca-certificates/corporate/ -name '.git*' -delete 2>/dev/null || true && \
    find /usr/local/share/ca-certificates/corporate/ -name 'README.md' -delete 2>/dev/null || true && \
    update-ca-certificates

ENV SSL_CERT_FILE=/etc/ssl/certs/ca-certificates.crt \
    SSL_CERT_DIR=/etc/ssl/certs \
    DOTNET_SYSTEM_NET_HTTP_USESOCKETSHTTPHANDLER=0 \
    NUGET_CERT_REVOCATION_MODE=off \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NUGET_SIGNATURE_VERIFICATION=false

# During epic/andy-settings-migration, Andy.Settings.Client is packed
# locally by `../andy-settings/scripts/pack-local.sh` and is not yet on
# nuget.org. docker-compose mounts that folder via additional_contexts
# so restore can pull the .nupkg from a local source. See rivoli-ai/conductor#771.
COPY --from=andy-settings-artifacts . /andy-settings-artifacts/
RUN printf '<?xml version="1.0" encoding="utf-8"?>\n\
<configuration>\n\
  <packageSources>\n\
    <clear />\n\
    <add key="andy-settings-local" value="/andy-settings-artifacts" />\n\
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />\n\
  </packageSources>\n\
</configuration>\n' > /build/NuGet.config

COPY Directory.Build.props Directory.Packages.props Andy.Rbac.sln ./
COPY proto/ proto/
COPY src/Andy.Rbac/Andy.Rbac.csproj src/Andy.Rbac/
COPY src/Andy.Rbac.Client/Andy.Rbac.Client.csproj src/Andy.Rbac.Client/
COPY src/Andy.Rbac.Infrastructure/Andy.Rbac.Infrastructure.csproj src/Andy.Rbac.Infrastructure/
COPY src/Andy.Rbac.Api/Andy.Rbac.Api.csproj src/Andy.Rbac.Api/
RUN dotnet restore src/Andy.Rbac.Api/Andy.Rbac.Api.csproj

COPY . .
RUN dotnet publish src/Andy.Rbac.Api/Andy.Rbac.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates curl openssl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .

RUN mkdir -p /https && \
    openssl req -x509 -nodes -days 3650 -newkey rsa:2048 \
      -keyout /tmp/dev.key -out /tmp/dev.crt \
      -subj "/CN=localhost" -addext "subjectAltName=DNS:localhost,IP:127.0.0.1" && \
    openssl pkcs12 -export -out /https/aspnetapp.pfx \
      -inkey /tmp/dev.key -in /tmp/dev.crt -passout pass:devcert && \
    rm -f /tmp/dev.key /tmp/dev.crt

RUN printf '#!/bin/sh\nset -e\nif ls /usr/local/share/ca-certificates/custom/*.crt 1>/dev/null 2>&1 || ls /usr/local/share/ca-certificates/custom/*.pem 1>/dev/null 2>&1; then\n    update-ca-certificates 2>/dev/null || true\nfi\nexec "$@"\n' > /docker-entrypoint.sh && \
    chmod +x /docker-entrypoint.sh

ENV SSL_CERT_FILE=/etc/ssl/certs/ca-certificates.crt \
    SSL_CERT_DIR=/etc/ssl/certs \
    ASPNETCORE_Kestrel__Certificates__Default__Path=/https/aspnetapp.pfx \
    ASPNETCORE_Kestrel__Certificates__Default__Password=devcert

EXPOSE 8080
ENTRYPOINT ["/docker-entrypoint.sh"]
CMD ["dotnet", "Andy.Rbac.Api.dll"]
