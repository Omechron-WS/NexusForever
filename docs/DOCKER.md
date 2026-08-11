# Local single-realm Docker stack

The Compose stack runs every NexusForever service required for one local realm while keeping all WildStar-derived assets outside this repository and outside the application image.

It is intended for development and in-world testing. It is not an internet-facing production deployment.

## Requirements

- Docker Engine with Docker Compose v2.
- A build-16042 TBL directory containing the extracted `.tbl` files and `en-US.bin`.
- A generated map directory containing `.nfmap` files.
- Optionally, a checked-out NexusForever.WorldDatabase directory whose files match the manifest embedded in the local image.
- A client-reachable IPv4 address for the Docker host.

Docker Desktop users must share the external asset directories with Docker. Linux users must ensure the container user can read every mounted file.

## Initialise

From the repository root, initialise the local environment and provide absolute host paths:

```bash
./scripts/nexusforever init \
    --tbl /absolute/path/to/tbl \
    --map /absolute/path/to/map \
    --world-sql /absolute/path/to/NexusForever.WorldDatabase \
    --realm-host 192.0.2.10
```

Omit `--world-sql` to migrate the seven schemas and bootstrap the account and realm without importing optional world content.

The command creates the ignored root `.env`, gives it mode `0600`, and generates independent credentials for MySQL, RabbitMQ, the two service APIs, and the initial game account. It never prints those credentials. Re-running `init` preserves existing values and fills only missing or placeholder secrets.

Review `.env` before starting. In particular:

- `NF_REALM_HOST` is written to realm 1 and is sent to the client. It must be a usable unicast IPv4 address; 0/8, loopback, multicast, and high reserved/broadcast ranges are rejected. Normal private/LAN addresses are accepted. AuthServer also connects to this same host and port when deciding whether the realm is online.
- `NF_BIND_ADDRESS` therefore cannot be loopback with bridge networking. Its default is `0.0.0.0`; restrict TCP ports 6600, 23115, and 24000 with the host firewall.
- `NF_WORLD_SQL_PATH` is optional. When present, it must be the external database root, not a copied directory under this repository.
- Password and service-credential values use only ASCII letters, digits, `_`, and `-`, keeping connection strings and the RabbitMQ URI unambiguous.

The checked-in [environment template](../deploy/docker/.env.example) contains no usable secret.

## Asset contract

Compose bind-mounts assets read-only:

| Host variable | Container path | Consumers |
|---|---|---|
| `NF_TBL_PATH` | `/assets/tbl` | WorldServer, ChatServer, FriendshipServer |
| `NF_MAP_PATH` | `/assets/map` | WorldServer |
| `NF_WORLD_SQL_PATH` | `/assets/world-sql` | One-shot database migrator only |

Nothing copies these directories into the repository, Docker build context, or image.

The host preflight checks `World.tbl`, `WordFilter.tbl`, `en-US.bin`, and the three default precached maps (`Western.nfmap`, `Eastern.nfmap`, and `NewCentral.nfmap`). WorldServer performs the complete game-table and map-header load during its own startup. French and German clients additionally need `fr-FR.bin` and `de-DE.bin`; they are not required for an English local stack.

World SQL is not recursively trusted. The one-shot migrator receives `/assets/world-sql` and validates it against the manifest embedded in the migrations assembly. It may apply only manifest-pinned compatible entries, in manifest order, with the importer’s transactional and replay checks.

**Current compatibility contract: 28 of the 30 pinned files are imported. Two whole files are excluded.** Their deletes and inserts are never partially applied:

- `Instance/Expedition/Evil from the Ether.sql` (world 3404) requires the unsupported `entity_script`, `entity_property`, and `creature_info_property` tables.
- `Instance/Tutorial/New Player Experience.sql` (world 3460) requires the unsupported `entity_script` table and `entity.Mode` column.

Leaving `NF_WORLD_SQL_PATH` unset produces no world-content import.

## Commands

```bash
# Validate host configuration, build and verify the one local fat image used by
# every .NET service, then validate the pinned world package without a database.
./scripts/nexusforever build

# Repeat host and image-backed world-package validation without building.
./scripts/nexusforever preflight

# Pull pinned MySQL/RabbitMQ images, build and verify the application image,
# start, wait for readiness, and run the complete infrastructure smoke check.
./scripts/nexusforever up

# Inspect all long-running and one-shot containers.
./scripts/nexusforever status

# Follow all logs, or only selected services.
./scripts/nexusforever logs
./scripts/nexusforever logs world-server auth-server

# Verify container health, seven databases, RabbitMQ consumers, and host TCP ports.
./scripts/nexusforever smoke

# Stop the stack but retain all named volumes.
./scripts/nexusforever down

# Delete the local MySQL, RabbitMQ, and cache volumes after an explicit prompt.
./scripts/nexusforever reset
```

For non-interactive disposable environments, `reset --yes` is the explicit destructive form. Reset never removes or modifies any operator-supplied TBL, map, or SQL path.

The helper accepts an alternate environment file and project name for isolated validation or disposable stacks:

```bash
NF_ENV_FILE=/absolute/path/to/test.env \
NF_COMPOSE_PROJECT_NAME=nexusforever-test \
    ./scripts/nexusforever up
```

The default project name is `nexusforever`, read from `.env`. `down` preserves that project’s named volumes. Only the confirmed `reset` command passes Compose’s volume-removal flag, scoped to the selected project and its project-labelled resources.

The smoke command checks infrastructure and process readiness, all seven schemas, exact realm-1 registration, the bootstrapped admin, and all five singleton consumers: the four worker queues plus `WorldServer_1`. When world SQL is configured, it also requires exactly 28 canonical imported-version records and verifies that the two excluded worlds 3404 and 3460 have no entity rows. It does not claim a build-16042 protocol login, character creation, map entry, or quest completion; those remain explicit client/in-world tests.

## Services and ports

Only three game-facing TCP listeners are published to the host:

| Service | Container port | Default host port |
|---|---:|---:|
| STS | 6600 | 6600 |
| Auth | 23115 | 23115 |
| World | 24000 | 24000 |

MySQL, RabbitMQ, its management API, Account API, and Character API remain reachable only on the Compose bridge network.

The long-running application services are:

- Account API and Character API.
- Singleton Character, Group, Chat, and Friendship workers.
- One WorldServer using realm ID 1 and queue `WorldServer_1`.
- STS and Auth.

Do not scale the workers or WorldServer. Their fixed RabbitMQ input queues and in-memory state require one instance in this local topology.

## Startup and readiness

Startup is deliberately ordered:

1. MySQL and RabbitMQ become healthy.
2. `database-create` idempotently creates all seven databases and grants the application user access.
3. `database-migrations` applies EF migrations, account bootstrap, realm-1 bootstrap, and any explicitly configured manifest-pinned world content.
4. Both authenticated APIs become healthy through real database queries.
5. The four singleton workers start.
6. `workers-ready` polls the RabbitMQ management API until all four pre-World worker queues report a live consumer.
7. WorldServer loads its external tables and maps, then opens TCP 24000.
8. Auth starts after WorldServer is healthy; STS can start independently after migration.

API health checks use their respective service credential and require a successful database-backed `200` or expected `404`. TCP health checks run inside the service containers. The operator smoke additionally requires live consumers on all four worker queues and `WorldServer_1`, then connects through each published host port.

Persistent state lives in named volumes:

- `mysql-data`
- `rabbitmq-data`
- `world-cache`
- `chat-cache`
- `friendship-cache`

Application containers use a read-only root filesystem, a bounded `/tmp` tmpfs, dropped Linux capabilities, and `no-new-privileges`. The three game-table caches are the only application write volumes.

## Configuration and image layout

All services use the same locally built image, selected by `NF_IMAGE` and defaulting to `nexusforever-local:dev`. Isolated working directories and commands select the executable:

```text
/app/migrations
/app/account-api
/app/character-api
/app/auth
/app/sts
/app/world
/app/character
/app/chat
/app/friendship
/app/group
```

Every executable still requires its service-specific JSON file beside the assembly. The image supplies non-secret configuration bases; Compose overrides credentials, connection strings, paths, ports, realm identity, and singleton queue names through environment variables. Compiled regional script DLLs sit beside WorldServer under `/app/world`; dynamic source compilation is disabled.

## Troubleshooting

- If Docker reports `failed to add the host ... veth ... operation not supported`, its running kernel cannot provide the bridge-network veth driver. On Linux, confirm `/lib/modules/$(uname -r)` exists; after a kernel package upgrade, reboot into the installed kernel before retrying the stack.
- If `preflight` rejects `NF_REALM_HOST`, choose a LAN or other IPv4 address that is reachable both from the client and from a bridge container through the published WorldServer port.
- If the pre-World `workers-ready` gate times out, inspect `character-server`, `group-server`, `chat-server`, and `friendship-server` logs, followed by RabbitMQ health. If the post-start smoke reports `WorldServer_1`, inspect WorldServer startup and its RabbitMQ connection.
- If WorldServer remains unhealthy, check the first fatal game-table or `.nfmap` load error. The source directories are mounted read-only, so fix or replace the external extraction rather than modifying files in a container.
- If `database-migrations` exits non-zero, no dependent application service starts. Inspect its logs before retrying; do not bypass the manifest or version checks.
- Changing `.env` database credentials is supported on the next startup because `database-create` reconciles the application user. Losing the root password while retaining `mysql-data` requires operator recovery or an explicitly confirmed reset.
