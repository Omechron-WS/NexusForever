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
| 1 — Loot | Incomplete | Schema, recursive loading, startup/ticking, owner attachment, authorised same-map collection, reconnect notification, loot bags, capacity-safe item grants, expiry, and stale-owner cleanup are live and tested. Conditional loot, group/raid allocation, and the Phase 6 `CorpseLooted` removal transition remain incomplete. |
| 2 — Spell variants | Incomplete | Factory dispatch is live. Failed-cast cleanup, cancellation semantics, phase masks, threshold input, aura cleanup, and exactly-once costs remain incomplete. |
| 3 — Client-side interaction | Incomplete | Foundations exist, but the build-16042 start/result packet loop, client correlation ID, timeout, entity callbacks, and quest integration are not live. |
| 4 — Combat, healing, and procs | Incomplete | Healing and immediate combat-state hooks exist. Proc collection, dispatch, cooldowns, and lifecycle cleanup are not integrated into the live combat loop. |
| 5 — NPC AI | Not implemented | Scheduled after the Phase 6 entity lifecycle is stable. |
| 6 — Entity vitals and lifecycle | Design only | Vitals, regeneration, death/corpse completion, rewards, respawn, class resources, sprint, and dash remain to be implemented. |
| 7 — Quests | Not implemented | Pending stable combat, lifecycle, and interaction systems. |
| 8 — Zone content | Not implemented | A starter/tutorial gameplay loop has not yet been proven. |
| 9 — Housing | Not implemented | Pending earlier gameplay phases. |
| 10 — Commands and quality of life | Not implemented | Pending security hardening of administrative command entry points. |
| 11 — Web | Not implemented | Existing endpoints require an authenticated service and browser perimeter before expansion. |

## Security and stability

Completed hardening:

- Bounded inbound and outbound game packets, bounded nested packed packets, and fail-closed game-packet dispatch.
- Bounded STS headers, bodies, SRP fields, and XML parsing, including correct fragmented and coalesced packet handling.
- Enforced STS authentication states with a pending state, authenticated-only token/account operations, and same-connection reauthentication support.
- Session encryption keys and account credentials are no longer written to authentication logs or exceptions.
- World shutdown awaits every player save, attempts both databases and every connected player after individual failures, and reports aggregate failure before declaring shutdown complete.
- Map updates isolate and report individual top-level map and instance failures in both synchronous and worker-task modes, allowing healthy siblings to keep ticking.
- Character and Account service APIs require bounded per-service credentials; typed clients attach them without redirect forwarding, and Aspire provisions separate persisted secrets.
- The administrative command WebSocket is disabled by default, bound to loopback in the example configuration, and no longer published by the default Aspire topology. When explicitly enabled it requires a hashed bearer credential and an exact Origin allow-list, accepts only bounded strict-UTF-8 text messages, and rejects malformed command envelopes without dispatching them.
- Task-backed events now distinguish successful, failed, and cancelled operations. Authentication and character mutations fail closed without running success callbacks, detached task failures are logged by default, and player cleanup retains its account lock while retrying failed saves.
- World loot tables now have an EF migration, are validated and loaded recursively at startup, and active loot expiry advances from the world tick. Invalid graph data fails startup without poisoning a later initialisation attempt.
- Player saves are serialised and acknowledge Auth and Character commits independently. Player/account state, inventory, mail, keybindings, entitlements, RBAC, guilds, and housing retain dirty state and deletion tombstones until their database commit succeeds; manager shutdown retries failed saves without blocking worker threads.
- Loot claims validate the stable character identity, client-supplied owner/item identifiers, live source entity, exact map instance, and range. Inventory-full claims remain retryable, reward-path failures use an at-most-once boundary, and unsupported loot-table item types fail startup rather than leaving permanent uncollectable drops.

Remaining priority work:

- Convert the remaining character child graphs (progression, loadouts, quests, and achievements) to post-commit acknowledgement; their compatibility overloads still clear dirty state while staging.
- Make cross-server lifecycle publication failures retryable; detached failures are now observable through error logging.
- Add bounded, non-blocking network send backpressure so a stalled client cannot stop the world update thread.
- Replace the command WebSocket role's effectively unrestricted permission set, bound its pending command queue, and serialise/bound outbound responses before restoring a browser console in Phase 11.

RC4 remains required by the build-16042 STS protocol. It is treated as a compatibility exception and contained through SRP state enforcement, bounded inputs, secret redaction, and deployment isolation.

SharpCompress `0.22.0` is transitively supplied by `Nexus.Archive 1.0.1` to the offline MapGenerator and has known advisories. A direct package override is unsafe because current SharpCompress releases changed the LZMA API. Resolution requires an updated or vendored Nexus.Archive plus smoke tests against real build-16042 archives.

## Next validated slices

1. Complete Phase 4 proc dispatch and combat-state hooks, then start the Phase 6 vital/death lifecycle.
2. Convert the remaining character child persistence graphs and make cross-server publication failures retryable.
3. Repair the Phase 2 and Phase 3 state-machine and client-correlation blockers; implement conditional loot alongside Phase 7 quest-state queries.
4. Add bounded network send backpressure and continue lower-risk long-uptime hardening alongside the gameplay phases.
