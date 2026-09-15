using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace KrokMPOptimization2;

/// <summary>
/// ghost-item guard: the host registry cull unregisters far objects and sends the
/// delete as a zero-length force-sync entry (real_obj == null).
/// that delete can be lost when queue eviction or the client packet budget intervenes.
/// this patch shields host tombstones and reconciles stale far world-items on clients.
/// stock Client_DeleteObject already destroys correctly, so this patch only observes it.
/// </summary>
internal static class ObjectCullPatch
{
    internal static bool TryApply(Harmony harmony)
    {
        try
        {
            var objectSyncType = AccessTools.TypeByName(
                "KrokoshaCasualtiesMP.NewCoolerObjectPacketWriteReadSystem");
            if (objectSyncType == null)
                return false;

            // use the Client_Object overload; stock removes the registry entry and destroys the object.
            var clientDelete = AccessTools.Method(objectSyncType, "Client_DeleteObject");
            if (clientDelete != null)
                harmony.Patch(clientDelete,
                    postfix: new HarmonyMethod(typeof(ObjectCullPatch), nameof(PostClientDelete)));

            // refresh the shield before ForceSyncQueueEvictPatch runs on the same Update.
            var update = AccessTools.Method(typeof(CoolSyncSubSystemForObjects), "Update");
            if (update != null)
                harmony.Patch(update,
                    prefix: new HarmonyMethod(typeof(ObjectCullPatch), nameof(PreEvictShield)));

            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[KrokMPOpt2] ObjectCullPatch apply failed: {ex.Message}");
            return false;
        }
    }

    internal static void PostClientDelete(object obj) =>
        ModTelemetry.Increment("clientDeleteObserved");

    // host prefix for CoolSyncSubSystemForObjects.Update.
    // tombstones are pending deletes, so refresh their age before the evictor runs.
    internal static void PreEvictShield(object __instance)
    {
        if (Plugin.Enabled == null || !Plugin.Enabled.Value || Net.is_client)
            return;
        if (!CoolSyncReflect.Ok || __instance == null)
            return;
        try
        {
            if (CoolSyncReflect.ServerObjects.GetValue(__instance) is not IDictionary servers)
                return;
            if (CoolSyncReflect.PerPlrStates.GetValue(__instance) is not IDictionary states)
                return;
            foreach (DictionaryEntry e in servers)
            {
                if (e.Value == null)
                    continue;
                if (CoolSyncReflect.SO_RealObj.GetValue(e.Value) != null)
                    continue;
                var netId = (knetid)CoolSyncReflect.SO_NetId.GetValue(e.Value);
                foreach (DictionaryEntry s in states)
                    ForceSyncAgeTracker.Forget(s.Value, netId); // reset age so the evictor keeps the entry
            }
        }
        catch
        {
            // cleanup failure must not interrupt the host update.
        }
    }
}

/// <summary>
/// client-side reconciler: destroys far world-items the host already culled but
/// whose 0-length delete packet was evicted or budgeted away. Never touches
/// player-body items, surface-inventory items, or near objects (same keep-rules
/// as host RegistryCull.ShouldKeepRegistered).
/// </summary>
internal sealed class CullReconcileSweep : MonoBehaviour
{
    private float _timer;

    internal static CullReconcileSweep Ensure(GameObject host)
    {
        var s = host.GetComponent<CullReconcileSweep>();
        return s != null ? s : host.AddComponent<CullReconcileSweep>();
    }

    private void Update()
    {
        if (Net.is_server && (Plugin.Enabled == null || !Plugin.Enabled.Value))
            return;
        float interval = Plugin.CullIntervalSeconds?.Value ?? 8f;
        if (interval <= 0f)
            return;
        _timer += Time.deltaTime;
        if (_timer < interval)
            return;
        _timer = 0f;

        try
        {
            float cullDist = Plugin.CullDistance?.Value ?? 240f;
            float sweepDist = cullDist + 40f;
            float sweepSq = sweepDist * sweepDist;
            var cam = Camera.main;
            if (cam == null)
                return;
            Vector3 camPos = cam.transform.position;
            int swept = 0;
            var snapshot = new List<KeyValuePair<GameObject, SyncInfo>>(NetObjectRegistry.SyncRegistry);
            foreach (var kv in snapshot)
            {
                var go = kv.Key;
                var si = kv.Value;
                if ((UnityEngine.Object)(object)go == (UnityEngine.Object)null
                    || si == null
                    || (UnityEngine.Object)(object)si.go == (UnityEngine.Object)null)
                    continue;
                if (!InventoryHelper.IsWorldItem(go))
                    continue; // never touch player-body or surface-inventory items
                Vector3 p = go.transform.position;
                float dx = p.x - camPos.x, dy = p.y - camPos.y;
                if (dx * dx + dy * dy < sweepSq)
                    continue; // only inspect objects the host would cull
                double age = Time.realtimeSinceStartupAsDouble - si.last_update_time;
                if (age < interval * 2)
                    continue; // recently updated objects are still being synced
                NetObjectRegistry.SyncRegistry.Remove(go);
                NetObjectRegistry.NetIdToSyncInfoDict.Remove(si.syncId);
                UnityEngine.Object.Destroy(go);
                if (++swept >= 32)
                    break;
            }
            if (swept > 0)
                ModTelemetry.AddRegistryCull(swept);
        }
        catch
        {
            // cleanup failure must not interrupt the client update.
        }
    }
}
