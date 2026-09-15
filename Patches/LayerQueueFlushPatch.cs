using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;

namespace KrokMPOptimization2;

[HarmonyPatch]
// clears stale queue state when a layer or transport lifecycle flushes objects.
internal static class LayerQueueFlushPatch
{
	static MethodBase TargetMethod()
	{
		var t = AccessTools.TypeByName("KrokoshaCasualtiesMP.RegenerateWorldPatch");
		return AccessTools.Method(t, "Prefix");
	}

	static void Postfix()
	{
		if (Plugin.Enabled == null || !Plugin.Enabled.Value || Net.is_client)
			return;

		double t0 = FrameTiming.Begin();
		try
		{
			object objectSync = CoolSyncReflect.ObjectInst?.GetValue(null);
			object charSync = CoolSyncReflect.CharInst?.GetValue(null);

			int objQ = 0;
			int charQ = 0;
			int invReanchored = 0;
			int npcDropped = 0;

			if (objectSync != null)
			{
				objQ = FlushSubsystem(objectSync, reanchorInventory: true, out int inv);
				invReanchored = inv;
			}

			if (charSync != null)
			{
				npcDropped = DeallocateNonPlayerBodies(charSync);
				charQ = FlushSubsystem(charSync, reanchorInventory: false, out _);
			}

			GcSlice.Apply(Plugin.GcSliceMs.Value);

			OptLog.Info(
				$"[KrokMPOpt2] layerFlush objQ={objQ} charQ={charQ} invReanchored={invReanchored} npcDropped={npcDropped}");
		}
		catch (Exception ex)
		{
			OptLog.Error("LayerQueueFlush", ex);
		}
		finally
		{
			FrameTiming.End("layerFlush", t0);
		}
	}

	private static int FlushSubsystem(object subsystem, bool reanchorInventory, out int invReanchored)
	{
		invReanchored = 0;
		int flushed = 0;
		if (CoolSyncReflect.PerPlrStates.GetValue(subsystem) is not IDictionary states)
			return 0;

		foreach (DictionaryEntry pe in states)
		{
			object perPlr = pe.Value;
			if (perPlr == null)
				continue;

			flushed += QueueOps.FlushSnapshotHistory(perPlr);
			QueueOps.ClearForceSyncQueue(perPlr);

			if (reanchorInventory)
				invReanchored += QueueOps.ReanchorInventoryOnly(subsystem, perPlr);

			if (CoolSyncReflect.PP_ClearQueued != null)
				CoolSyncReflect.PP_ClearQueued.SetValue(perPlr, false);
		}

		return flushed;
	}

	private static int DeallocateNonPlayerBodies(object charSync)
	{
		int npc = 0;
		if (CoolSyncReflect.ServerObjects.GetValue(charSync) is not IDictionary objs)
			return 0;

		var toDrop = new List<object>(64);
		foreach (DictionaryEntry e in objs)
		{
			object so = e.Value;
			object real = so != null ? CoolSyncReflect.SO_RealObj.GetValue(so) : null;
			if (real is NetBody body && body.is_player)
				continue;
			toDrop.Add(e.Key);
		}

		foreach (object id in toDrop)
		{
			try
			{
				CoolSyncReflect.Deallocate.Invoke(charSync, new[] { id });
				npc++;
			}
			catch
			{
				// ignore
			}
		}

		return npc;
	}
}
