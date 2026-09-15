# KrokMPOptimization2

BepInEx plugin for **Casualties Unknown** + **KrokMP**. Host-side **SyncProducer** (replaces stock FastSync) plus **client relief** (transport inbox budget, local FPS sync scale) baked in since v2.1.

## Requirements

- BepInEx + KrokMP (`KrokoshaCasualtiesMP`)

## Install

| Role | What to install |
|------|-----------------|
| **Host** | `KrokMPOptimization2.dll` - leave `[General] Enabled=true` (default) |
| **Weak client** | Same DLL - set `[General] Enabled=false`, keep `[ClientRelief] Enabled=true` |

1. Place `KrokMPOptimization2.dll` in `BepInEx/plugins/KrokMPOptimization2/`.
2. Check `Player.log` for `[KrokMPOpt2] loaded`.

Config: `BepInEx/config/com.local.krokmp.optimization2.cfg`

## What the mod does

This mod keeps multiplayer sync work bounded and favors useful updates when the host or a client is under load.

- **Host producer:** replaces the broad FastSync gather with dirty tracking, priority lanes, a join burst, and a low-rate safety net.
- **Queue protection:** caps snapshot history, trims overflow, recovers stale queue heads, and drains acknowledged history in small steps.
- **Client relief:** reuses transport buffers, limits packets per frame, avoids unchanged ID maps, and scales sync work to local frame time.
- **Memory cleanup:** reuses CoolSync buffers, compression scratch space, and writers while pruning stale object state and cached resources.
- **Visibility:** records queue depth, producer work, frame time, and targeted memory counters so changes can be checked in `Player.log`.

## Client relief

Former **KrokMPClientRelief** patches, now under `[ClientRelief]`:

| Patch | What it does |
|-------|----------------|
| Transport poll buffer | Reuses `IntPtr[]` instead of `new IntPtr[65535]` every frame |
| Message budget | Caps Steam packets processed per frame on clients (default 256) |
| Lazy ID dicts | Skips `UpdateTheIDDicts` when player set unchanged |
| Local FPS scale | `AdaptiveSyncTimerDelta` uses `min(hostScale, localFpsScale)` |
| Registry throttle | Staggers `NetObjectRegistry` client polls when FPS is low |


## Host producer (unchanged)

| v1 | v2 |
|----|-----|
| Tunes FastSync gather | **Disables** `Server_RunFastSync` entirely |
| Registry poll unchanged | Suppresses fast registry gather; slow mode + cull only |
| Registry cull | CPUOpt authority chunks when installed (farthest-first); else `CullDistance` Euclidean |
| Queue safety on object-family subsystems | Queue safety on **all** CoolSync queues |
| - | SyncProducer lanes + safety net + join burst + container-open sync |

## Log markers

- `[KrokMPOpt2] loaded` - plugin active
- `[KrokMPOpt2] client relief armed` - client patches registered
- `[KrokMPOpt2] host transport active` - SyncProducer engaged (host only)
- `[KrokMPOpt2] fastsync_disabled` - stock FastSync skipped
