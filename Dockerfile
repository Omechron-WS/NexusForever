# syntax=docker/dockerfile:1.7

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-noble AS build

ARG BUILD_CONFIGURATION=Release

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

WORKDIR /src

# The build context deliberately contains source and tracked configuration templates only.
# Operator-supplied game tables, maps, archives, and world SQL are excluded by
# .dockerignore and are mounted into containers at runtime.
COPY Source/ ./Source/

RUN --mount=type=cache,id=nexusforever-nuget,target=/root/.nuget/packages \
    dotnet restore Source/NexusForever.slnx \
        -m:1 \
        --disable-build-servers

RUN --mount=type=cache,id=nexusforever-nuget,target=/root/.nuget/packages \
    set -eux; \
    publish_project() { \
        project="$1"; \
        output="$2"; \
        dotnet publish "$project" \
            --configuration "$BUILD_CONFIGURATION" \
            --no-restore \
            --no-self-contained \
            --output "$output" \
            -m:1 \
            --disable-build-servers \
            -p:UseAppHost=false \
            -p:PublishAot=false \
            -p:PublishSingleFile=false \
            -p:PublishTrimmed=false; \
    }; \
    publish_project Source/NexusForever.Aspire.Database.Migrations/NexusForever.Aspire.Database.Migrations.csproj /out/migrations; \
    publish_project Source/NexusForever.API.Account/NexusForever.API.Account.csproj /out/account-api; \
    publish_project Source/NexusForever.API.Character/NexusForever.API.Character.csproj /out/character-api; \
    publish_project Source/NexusForever.AuthServer/NexusForever.AuthServer.csproj /out/auth; \
    publish_project Source/NexusForever.StsServer/NexusForever.StsServer.csproj /out/sts; \
    publish_project Source/NexusForever.WorldServer/NexusForever.WorldServer.csproj /out/world; \
    publish_project Source/NexusForever.Server.Character/NexusForever.Server.Character.csproj /out/character; \
    publish_project Source/NexusForever.Server.ChatServer/NexusForever.Server.ChatServer.csproj /out/chat; \
    publish_project Source/NexusForever.Server.Friendship/NexusForever.Server.Friendship.csproj /out/friendship; \
    publish_project Source/NexusForever.Server.GroupServer/NexusForever.Server.GroupServer.csproj /out/group; \
    for script_project in \
        Alizar \
        Arcterra \
        Farside \
        Instance \
        Isigrol \
        Main \
        Olyssia; \
    do \
        dotnet build "Source/NexusForever.Script.${script_project}/NexusForever.Script.${script_project}.csproj" \
            --configuration "$BUILD_CONFIGURATION" \
            --no-restore \
            -m:1 \
            --disable-build-servers \
            -p:SolutionDir=/src/Source/; \
        script_dll="/src/Source/NexusForever.WorldServer/bin/${BUILD_CONFIGURATION}/net10.0/NexusForever.Script.${script_project}.dll"; \
        test -s "$script_dll"; \
        cp "$script_dll" /out/world/; \
    done

RUN set -eux; \
    install_runtime_config() { \
        service="$1"; \
        source_directory="$2"; \
        config_name="$3"; \
        install -m 0644 \
            "Source/${source_directory}/${config_name}.example.json" \
            "/out/${service}/${config_name}.json"; \
        install -m 0644 \
            "Source/${source_directory}/nlog.config" \
            "/out/${service}/nlog.config"; \
    }; \
    install_runtime_config migrations NexusForever.Aspire.Database.Migrations AspireMigrations; \
    install_runtime_config account-api NexusForever.API.Account AccountAPI; \
    install_runtime_config character-api NexusForever.API.Character CharacterAPI; \
    install_runtime_config auth NexusForever.AuthServer AuthServer; \
    install_runtime_config sts NexusForever.StsServer StsServer; \
    install_runtime_config world NexusForever.WorldServer WorldServer; \
    install_runtime_config character NexusForever.Server.Character CharacterServer; \
    install_runtime_config chat NexusForever.Server.ChatServer ChatServer; \
    install_runtime_config friendship NexusForever.Server.Friendship FriendshipServer; \
    install_runtime_config group NexusForever.Server.GroupServer GroupServer; \
    find /out -type f -name '*.example.json' -delete; \
    forbidden_file="$(find /out -type f \( \
        -iname '*.archive' -o \
        -iname '*.bin' -o \
        -iname '*.db' -o \
        -iname '*.index' -o \
        -iname '*.nfmap' -o \
        -iname '*.sql' -o \
        -iname '*.tbl' \
    \) -print -quit)"; \
    test -z "$forbidden_file"; \
    set -- /out/world/NexusForever.Script.*.dll; \
    test "$#" -eq 7

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-noble AS runtime

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

WORKDIR /app

# Package versions follow the pinned Ubuntu/.NET base-image snapshot.
# hadolint ignore=DL3008
RUN apt-get update \
    && apt-get install --yes --no-install-recommends \
        curl \
        netcat-openbsd \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p \
        /assets/map \
        /assets/tbl \
        /assets/world-sql \
        /var/cache/nexusforever/chat \
        /var/cache/nexusforever/friendship \
        /var/cache/nexusforever/world \
    && chown -R app:app /var/cache/nexusforever

COPY --from=build --chown=app:app /out/ /app/

EXPOSE 4000 4001 5000 6600 23115 24000

# The official .NET image provides the stable non-root `app` account.
# hadolint ignore=DL3066
USER app

ENTRYPOINT ["dotnet"]
CMD ["/app/world/NexusForever.WorldServer.dll"]
