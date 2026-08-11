#!/bin/sh

set -eu

: "${RABBITMQ_MANAGEMENT_URL:?RABBITMQ_MANAGEMENT_URL is required}"
: "${RABBITMQ_USER:?RABBITMQ_USER is required}"
: "${RABBITMQ_PASSWORD:?RABBITMQ_PASSWORD is required}"

timeout_seconds=${WORKER_READY_TIMEOUT_SECONDS:-180}
case "$timeout_seconds" in
    *[!0-9]* | "" | "0")
        echo "WORKER_READY_TIMEOUT_SECONDS must be a positive integer." >&2
        exit 2
        ;;
esac

if [ "$#" -eq 0 ]; then
    set -- CharacterServer ChatServer FriendshipServer GroupServer
fi

for queue in "$@"; do
    case "$queue" in
        *[!A-Za-z0-9_.:-]* | "")
            echo "RabbitMQ queue names must contain only ASCII letters, digits, underscores, dots, colons, or dashes." >&2
            exit 2
            ;;
    esac
done

started_at=$(date +%s)
last_reported_at=$started_at

while :; do
    pending=""

    for queue in "$@"; do
        response=""
        if response=$(curl \
            --fail \
            --silent \
            --show-error \
            --connect-timeout 2 \
            --max-time 5 \
            --user "${RABBITMQ_USER}:${RABBITMQ_PASSWORD}" \
            "${RABBITMQ_MANAGEMENT_URL}/api/queues/%2F/${queue}" 2>/dev/null) \
            && printf '%s' "$response" \
                | grep -Eq '"consumers"[[:space:]]*:[[:space:]]*[1-9][0-9]*'; then
            continue
        fi

        pending="${pending}${pending:+, }${queue}"
    done

    if [ -z "$pending" ]; then
        echo "All requested NexusForever queues have attached RabbitMQ consumers."
        exit 0
    fi

    now=$(date +%s)
    elapsed=$((now - started_at))
    if [ "$elapsed" -ge "$timeout_seconds" ]; then
        echo "Timed out waiting for RabbitMQ consumers: ${pending}." >&2
        exit 1
    fi

    if [ $((now - last_reported_at)) -ge 10 ]; then
        echo "Waiting for RabbitMQ consumers: ${pending}."
        last_reported_at=$now
    fi

    sleep 2
done
