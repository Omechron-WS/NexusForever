# Convergence Status

Last updated: 10 August 2026

This document records verified implementation status on the `convergence` branch. A phase is complete only when its live integration, failure handling, lifecycle cleanup, protocol compatibility, focused tests, and full-solution validation have been demonstrated.

## Baseline

- `origin/game_rework` at `dade7f0d` was merged into `convergence` without rewriting published history.
- The merge moved the repository to `Source/NexusForever.slnx` and retained both convergence test projects.
- Build-16042 community privacy wire values remain encoded in one bit as `Public = 0` and `Private = 1`.
- The full solution builds and all current automated tests pass. SharpCompress advisories in the offline map-generation dependency remain documented below.

## Roadmap status

| Phase | Status | Verified position |
|---|---|---|
| 0 — Static types | Conditional | Planned types are present and tested. Some downstream use is absent, and `TelegraphDamageFlag` values still require capture validation. |
| 1 — Loot | Incomplete | Schema, recursive loading, startup/ticking, owner attachment, authorised same-map collection, reconnect notification, loot bags, capacity-safe item grants, expiry, stale-owner cleanup, and final-package corpse cleanup are live and tested. Conditional loot and group/raid allocation remain incomplete. |
| 2 — Spell variants | Incomplete | Factory dispatch is live. Failed-cast cleanup, cancellation semantics, phase masks, threshold input, aura cleanup, and exactly-once costs remain incomplete. |
| 3 — Client-side interaction | Incomplete | Foundations exist, but the build-16042 start/result packet loop, client correlation ID, timeout, entity callbacks, and quest integration are not live. |
| 4 — Combat, healing, and procs | Incomplete | Healing, live movement/critical proc dispatch, queued triggers, exact per-spell/per-target teardown across finish, cancellation, aura exit, death, and disposal, combat-state transitions, and corrected critical, power, and mitigation formula paths are live and tested. Proc chance, build-16042 internal-cooldown semantics, `OnHit`/`OnDamageReceived`, and PvP threat-timeout coverage remain incomplete. |
| 5 — NPC AI | Not implemented | Scheduled after the Phase 6 entity lifecycle is stable. |
| 6 — Entity vitals and lifecycle | In progress | Wire-compatible vital aliases, focus property data, persistence-safe initial pools, deterministic unit/class regeneration, idempotent death and rewards, corpse cleanup, and persistent non-player respawn are live and tested. Spell resource requirements/costs, sprint, dash, and the remaining class-resource action hooks are incomplete. |
| 7 — Quests | In progress | Quest and achievement persistence is commit-aware. Item, money, XP, and reputation rewards are prevalidated with aggregate inventory exchange and duplicate-completion guards. Objective ordering/checklists, live event adapters, communicator flow, timers/repeats/sharing, remaining reward domains, and durable cross-domain reward delivery remain incomplete. |
| 8 — Zone content | Not implemented | A starter/tutorial gameplay loop has not yet been proven. |
| 9 — Housing | Not implemented | Pending earlier gameplay phases. |
| 10 — Commands and quality of life | In progress | The administrative command transport now has an explicit permission allow-list and bounded command/response queues. Gameplay and operator command expansion remains incomplete. |
| 11 — Web | Foundations only | Service APIs and the command WebSocket have authenticated, fail-closed perimeters. No production web portal or browser console has been implemented. |

## Security and stability

Completed hardening:

- Bounded inbound game packets, bounded nested packed packets, and fail-closed game-packet dispatch.
- A per-session bounded asynchronous writer serialises complete game and STS frames, handles partial sends, applies byte/frame backpressure without blocking the world thread, and makes disconnect/drain state atomic.
- Bounded STS headers, bodies, SRP fields, and XML parsing, including correct fragmented and coalesced packet handling.
- Enforced STS authentication states with a pending state, authenticated-only token/account operations, and same-connection reauthentication support.
- Session encryption keys and account credentials are no longer written to authentication logs or exceptions.
- World shutdown awaits every player save, attempts both databases and every connected player after individual failures, and reports aggregate failure before declaring shutdown complete.
- Map updates isolate and report individual top-level map and instance failures in both synchronous and worker-task modes, allowing healthy siblings to keep ticking.
- Character and Account service APIs require bounded per-service credentials; typed clients attach them without redirect forwarding, and Aspire provisions separate persisted secrets.
- The administrative command WebSocket is disabled by default, bound to loopback in the example configuration, and no longer published by the default Aspire topology. When explicitly enabled it requires a hashed bearer credential and exact Origin and permission allow-lists, accepts only bounded strict-UTF-8 text messages, bounds the global delayed-command queue, and serialises bounded per-connection responses through one observed writer.
- Task-backed events now distinguish successful, failed, and cancelled operations. Authentication and character mutations fail closed without running success callbacks, detached task failures are logged by default, and player cleanup retains its account lock while retrying failed saves.
- World loot tables now have an EF migration, are validated and loaded recursively at startup, and active loot expiry advances from the world tick. Invalid graph data fails startup without poisoning a later initialisation attempt.
- Player saves are serialised and acknowledge Auth and Character commits independently. Player/account state, inventory, mail, character stats and scalar progression, presentation and loadout graphs, quests, achievements, keybindings, entitlements, RBAC, guilds, and housing retain dirty state and deletion tombstones until their database commit succeeds; manager shutdown retries failed saves without blocking worker threads.
- Loot claims validate the stable character identity, client-supplied owner/item identifiers, live source entity, exact map instance, and range. Inventory-full claims remain retryable, reward-path failures use an at-most-once boundary, and unsupported loot-table item types fail startup rather than leaving permanent uncollectable drops.
- Quest reward selection, eligibility, currency/reputation/XP bounds, and aggregate inventory capacity are validated before mutation; pushed-item removal and item grants use one exact-bag exchange and repeated or re-entrant completion is rejected.

Remaining priority work:

- Make cross-server lifecycle publication failures retryable; detached failures are now observable through error logging.
- Add a durable quest reward outbox/completion record before enabling account reward domains; the current in-memory admission boundary cannot make a process crash atomic across inventory, character, and account stores.
- Replace the vulnerable offline archive dependency with an updated or vendored implementation and verify LZMA reads against real build-16042 client archives.
- Finish the Phase 2/3 spell-state and client-side-interaction failure paths before relying on them for scripted quest interactions.

RC4 remains required by the build-16042 STS protocol. It is treated as a compatibility exception and contained through SRP state enforcement, bounded inputs, secret redaction, and deployment isolation.

SharpCompress `0.22.0` is transitively supplied by `Nexus.Archive 1.0.1` to the offline MapGenerator and has known advisories. A direct package override is unsafe because current SharpCompress releases changed the LZMA API. Resolution requires an updated or vendored Nexus.Archive plus smoke tests against real build-16042 archives.

## Next validated slices

1. Close the remaining Phase 4 proc-chance, direct-hit, received-damage, and PvP threat-timeout gaps; then wire Phase 6 spell resource costs, dash, and sprint.
2. Complete ordered/checklist quest objectives and their verified live event adapters, then communicator, timer, repeat, sharing, and durable reward-delivery slices.
3. Repair the Phase 2 and Phase 3 spell-state and build-16042 client-correlation blockers, then implement minimum viable NPC aggro, targeting, attacks, movement, leash, and evade on the stable lifecycle.
4. Replace the archive dependency, make cross-server lifecycle publication retryable, and continue lower-risk long-uptime hardening alongside the gameplay phases.
