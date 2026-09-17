FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:6a94333d37514e385650a3c81a55e5350b67253dbe136e9cf17e499c35606a8c AS base
USER $APP_UID
WORKDIR /app

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0@sha256:2fa828c68761b1b8c23d7662dc134421b9d3b59fe1425fdbc80804e390cdb24d AS build
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH
WORKDIR /src
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/Event.MigrationService/Event.MigrationService.csproj", "src/Event.MigrationService/packages.lock.json", "src/Event.MigrationService/"]
COPY ["src/Explore.Atproto.Transport/Explore.Atproto.Transport.csproj", "src/Explore.Atproto.Transport/packages.lock.json", "src/Explore.Atproto.Transport/"]
COPY ["src/Explore.Application/Explore.Application.csproj", "src/Explore.Application/packages.lock.json", "src/Explore.Application/"]
COPY ["src/Explore.Domain/Explore.Domain.csproj", "src/Explore.Domain/packages.lock.json", "src/Explore.Domain/"]
COPY ["src/Explore.Infrastructure/Explore.Infrastructure.csproj", "src/Explore.Infrastructure/packages.lock.json", "src/Explore.Infrastructure/"]
COPY ["src/Explore.Persistence/Explore.Persistence.csproj", "src/Explore.Persistence/packages.lock.json", "src/Explore.Persistence/"]
COPY ["src/Explore.Persistence.PrivacyErasureAuthority.Migrations.Sqlite/Explore.Persistence.PrivacyErasureAuthority.Migrations.Sqlite.csproj", "src/Explore.Persistence.PrivacyErasureAuthority.Migrations.Sqlite/packages.lock.json", "src/Explore.Persistence.PrivacyErasureAuthority.Migrations.Sqlite/"]
COPY ["src/Explore.Persistence.DataProtection.Migrations.MySql/Explore.Persistence.DataProtection.Migrations.MySql.csproj", "src/Explore.Persistence.DataProtection.Migrations.MySql/packages.lock.json", "src/Explore.Persistence.DataProtection.Migrations.MySql/"]
COPY ["src/Explore.Persistence.DataProtection.Migrations.Sqlite/Explore.Persistence.DataProtection.Migrations.Sqlite.csproj", "src/Explore.Persistence.DataProtection.Migrations.Sqlite/packages.lock.json", "src/Explore.Persistence.DataProtection.Migrations.Sqlite/"]
COPY ["src/Explore.Persistence.DataProtection.Migrations.SqlServer/Explore.Persistence.DataProtection.Migrations.SqlServer.csproj", "src/Explore.Persistence.DataProtection.Migrations.SqlServer/packages.lock.json", "src/Explore.Persistence.DataProtection.Migrations.SqlServer/"]
COPY ["src/Explore.Persistence.Migrations.MySql/Explore.Persistence.Migrations.MySql.csproj", "src/Explore.Persistence.Migrations.MySql/packages.lock.json", "src/Explore.Persistence.Migrations.MySql/"]
COPY ["src/Explore.Persistence.Migrations.Sqlite/Explore.Persistence.Migrations.Sqlite.csproj", "src/Explore.Persistence.Migrations.Sqlite/packages.lock.json", "src/Explore.Persistence.Migrations.Sqlite/"]
COPY ["src/Explore.Persistence.Migrations.SqlServer/Explore.Persistence.Migrations.SqlServer.csproj", "src/Explore.Persistence.Migrations.SqlServer/packages.lock.json", "src/Explore.Persistence.Migrations.SqlServer/"]
COPY ["src/Explore.Secrets/Explore.Secrets.csproj", "src/Explore.Secrets/packages.lock.json", "src/Explore.Secrets/"]
COPY ["src/Explore.ServiceDefaults/Explore.ServiceDefaults.csproj", "src/Explore.ServiceDefaults/packages.lock.json", "src/Explore.ServiceDefaults/"]
RUN dotnet restore "src/Event.MigrationService/Event.MigrationService.csproj" -a $TARGETARCH --locked-mode
COPY . .
WORKDIR "/src/src/Event.MigrationService"
RUN dotnet publish "Event.MigrationService.csproj" -c $BUILD_CONFIGURATION -o /app/publish -a $TARGETARCH /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --chown=$APP_UID:$APP_UID --from=build /src/deploy/bootstrap/README.md /etc/islamu-event/bootstrap/README.md
COPY --chown=$APP_UID:$APP_UID --from=build /src/schemas/configuration-manifest-v1alpha2.schema.json /app/schemas/configuration-manifest-v1alpha2.schema.json
COPY --chown=$APP_UID:$APP_UID --from=build /src/schemas/tenant-configuration-package-v1alpha2.schema.json /app/schemas/tenant-configuration-package-v1alpha2.schema.json
ENTRYPOINT ["dotnet", "Event.MigrationService.dll"]
