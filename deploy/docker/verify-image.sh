#!/usr/bin/env bash

set -euo pipefail

if [[ $# -ne 1 ]]; then
    echo "Usage: $0 <image>" >&2
    exit 2
fi

image="$1"

if ! command -v docker >/dev/null 2>&1; then
    echo "docker is required to verify the image" >&2
    exit 2
fi

docker image inspect "$image" >/dev/null

image_user="$(docker image inspect --format '{{.Config.User}}' "$image")"
if [[ -z "$image_user" || "$image_user" == "0" || "$image_user" == "root" ]]; then
    echo "image must declare a non-root user (found '${image_user:-<empty>}')" >&2
    exit 1
fi

workdir="$(docker image inspect --format '{{.Config.WorkingDir}}' "$image")"
if [[ "$workdir" != "/app" ]]; then
    echo "image working directory must be /app (found '$workdir')" >&2
    exit 1
fi

entrypoint="$(docker image inspect --format '{{json .Config.Entrypoint}}' "$image")"
if [[ "$entrypoint" != '["dotnet"]' ]]; then
    echo "image entrypoint must be [\"dotnet\"] (found '$entrypoint')" >&2
    exit 1
fi

default_command="$(docker image inspect --format '{{json .Config.Cmd}}' "$image")"
if [[ "$default_command" != '["/app/world/NexusForever.WorldServer.dll"]' ]]; then
    echo "image default command must launch WorldServer (found '$default_command')" >&2
    exit 1
fi

docker run --rm --network none --entrypoint /bin/sh "$image" -eu -c '
check_service()
{
    service="$1"
    assembly="$2"
    config="$3"
    directory="/app/${service}"

    test -d "$directory"
    test -s "${directory}/${assembly}.dll"
    test -s "${directory}/${assembly}.deps.json"
    test -s "${directory}/${assembly}.runtimeconfig.json"
    test -s "${directory}/${config}.json"
    test -s "${directory}/nlog.config"
}

check_service migrations NexusForever.Aspire.Database.Migrations AspireMigrations
check_service account-api NexusForever.API.Account AccountAPI
check_service character-api NexusForever.API.Character CharacterAPI
check_service auth NexusForever.AuthServer AuthServer
check_service sts NexusForever.StsServer StsServer
check_service world NexusForever.WorldServer WorldServer
check_service character NexusForever.Server.Character CharacterServer
check_service chat NexusForever.Server.ChatServer ChatServer
check_service friendship NexusForever.Server.Friendship FriendshipServer
check_service group NexusForever.Server.GroupServer GroupServer

for script in Alizar Arcterra Farside Instance Isigrol Main Olyssia; do
    test -s "/app/world/NexusForever.Script.${script}.dll"
done

script_count="$(find /app/world -maxdepth 1 -type f -name "NexusForever.Script.*.dll" | wc -l | tr -d "[:space:]")"
test "$script_count" -eq 7
test -s /app/world/NexusForever.Script.dll
test -s /app/world/Basic.Reference.Assemblies.Net100.dll
test -s /app/world/Microsoft.CodeAnalysis.CSharp.dll

for cache in chat friendship world; do
    test -d "/var/cache/nexusforever/${cache}"
    test -w "/var/cache/nexusforever/${cache}"
done

test -d /assets/map
test -d /assets/tbl
test -d /assets/world-sql
test -z "$(find /assets -type f -print -quit)"

forbidden_file="$(find /app -type f \( \
    -iname "*.archive" -o \
    -iname "*.bin" -o \
    -iname "*.db" -o \
    -iname "*.db3" -o \
    -iname "*.index" -o \
    -iname "*.nfmap" -o \
    -iname "*.sqlite" -o \
    -iname "*.sqlite3" -o \
    -iname "*.sql" -o \
    -iname "*.tbl" \
\) -print -quit)"
test -z "$forbidden_file"

dotnet --list-runtimes | grep -q "Microsoft.AspNetCore.App 10\."
dotnet --list-runtimes | grep -q "Microsoft.NETCore.App 10\."
command -v curl >/dev/null
command -v nc >/dev/null
'

echo "Image contract verified: $image"
