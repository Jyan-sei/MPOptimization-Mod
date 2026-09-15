using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace KrokMPOptimization2;

[HarmonyPatch(typeof(NetObjectRegistry), "Server_QueueSync")]
// routes registry sync events into the producer's dirty lanes.
internal static class EventHookPatches
{
	static void Postfix(SyncInfo si)
	{
		if (Plugin.Enabled == null || !Plugin.Enabled.Value || Net.is_client || si == null)
			return;

		DirtyTracker.MarkDirtyAllPlayers(si.syncId, SyncLane.Hot, SyncReason.Event);
		ModTelemetry.Increment("dirty_enqueued");
	}
}

[HarmonyPatch(typeof(NetObjectRegistry), "Server_QueueSyncForOne")]
internal static class EventHookForOnePatch
{
	static void Postfix(SyncInfo si, NetPlayer plr)
	{
		if (Plugin.Enabled == null || !Plugin.Enabled.Value || Net.is_client || si == null || plr == null)
			return;

		DirtyTracker.MarkDirty(plr.clientId, si.syncId, SyncLane.Hot, SyncReason.Event);
		ModTelemetry.Increment("dirty_enqueued");
	}
}

[HarmonyPatch(typeof(NetObjectRegistry), "Server_ObjectSyncSingle")]
internal static class EventHookObjectSyncSinglePatch
{
	static void Postfix(GameObject obj)
	{
		if (Plugin.Enabled == null || !Plugin.Enabled.Value || Net.is_client || obj == null)
			return;
		if (!NetObjectRegistry.TryGetSyncInfo(obj, out SyncInfo si) || si == null)
			return;

		DirtyTracker.MarkDirtyAllPlayers(si.syncId, SyncLane.Hot, SyncReason.Event);
		ModTelemetry.Increment("dirty_enqueued");
	}
}
