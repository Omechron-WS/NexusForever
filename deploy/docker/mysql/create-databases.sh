#!/bin/sh

set -eu

export LC_ALL=C

: "${MYSQL_HOST:?MYSQL_HOST is required}"
: "${MYSQL_ROOT_PASSWORD:?MYSQL_ROOT_PASSWORD is required}"
: "${MYSQL_USER:?MYSQL_USER is required}"
: "${MYSQL_PASSWORD:?MYSQL_PASSWORD is required}"

case "$MYSQL_USER" in
    *[!A-Za-z0-9_]* | "")
        echo "MYSQL_USER must contain only ASCII letters, digits, or underscores." >&2
        exit 2
        ;;
esac

if [ "${#MYSQL_USER}" -gt 32 ]; then
    echo "MYSQL_USER must contain no more than 32 characters." >&2
    exit 2
fi

case "$MYSQL_PASSWORD" in
    *[!A-Za-z0-9_-]* | "")
        echo "MYSQL_PASSWORD must contain only base64url-safe characters." >&2
        exit 2
        ;;
esac

MYSQL_PWD=$MYSQL_ROOT_PASSWORD
export MYSQL_PWD

mysql \
    --protocol=TCP \
    --host="$MYSQL_HOST" \
    --user=root \
    --connect-timeout=10 <<SQL
CREATE DATABASE IF NOT EXISTS nexus_forever_auth CHARACTER SET utf8mb4;
CREATE DATABASE IF NOT EXISTS nexus_forever_character CHARACTER SET utf8mb4;
CREATE DATABASE IF NOT EXISTS nexus_forever_world CHARACTER SET utf8mb4;
CREATE DATABASE IF NOT EXISTS nexus_forever_group CHARACTER SET utf8mb4;
CREATE DATABASE IF NOT EXISTS nexus_forever_chat CHARACTER SET utf8mb4;
CREATE DATABASE IF NOT EXISTS nexus_forever_friendship CHARACTER SET utf8mb4;
CREATE DATABASE IF NOT EXISTS nexus_forever_query CHARACTER SET utf8mb4;

CREATE USER IF NOT EXISTS '${MYSQL_USER}'@'%' IDENTIFIED BY '${MYSQL_PASSWORD}';
ALTER USER '${MYSQL_USER}'@'%' IDENTIFIED BY '${MYSQL_PASSWORD}';

GRANT ALL PRIVILEGES ON nexus_forever_auth.* TO '${MYSQL_USER}'@'%';
GRANT ALL PRIVILEGES ON nexus_forever_character.* TO '${MYSQL_USER}'@'%';
GRANT ALL PRIVILEGES ON nexus_forever_world.* TO '${MYSQL_USER}'@'%';
GRANT ALL PRIVILEGES ON nexus_forever_group.* TO '${MYSQL_USER}'@'%';
GRANT ALL PRIVILEGES ON nexus_forever_chat.* TO '${MYSQL_USER}'@'%';
GRANT ALL PRIVILEGES ON nexus_forever_friendship.* TO '${MYSQL_USER}'@'%';
GRANT ALL PRIVILEGES ON nexus_forever_query.* TO '${MYSQL_USER}'@'%';
SQL

unset MYSQL_PWD
echo "NexusForever databases and application grants are ready."
