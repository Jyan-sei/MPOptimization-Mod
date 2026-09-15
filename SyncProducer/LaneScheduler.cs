using System.Collections.Generic;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace KrokMPOptimization2;

// drains hot, near, and dormant dirty lanes within per-player queue budgets.
internal static class LaneScheduler
{
	internal static int FlushAll()
	{
		object syncInst = CoolSyncReflect.GetObjectSyncInst();
		if (syncInst == null)
			return 0;

		int hotCap = Plugin.HotLaneCapPerTick?.Value ?? 24;
		int nearCap = Plugin.NearLaneCapPerTick?.Value ?? 12;
		int dormantCap = Plugin.DormantLaneCapPerTick?.Value ?? 4;
		int total = 0;

		try
		{
			foreach (NetPlayer plr in NetPlayer.AllLivingPlayers)
			{
				if (!NetPlayer.TryGetNetPlayerAndNetBodyFromClientId(plr.clientId, out _, out _))
					continue;

				total += FlushLane(syncInst, plr, SyncLane.Hot, hotCap);
				total += FlushLane(syncInst, plr, SyncLane.Near, nearCap);
				total += FlushLane(syncInst, plr, SyncLane.Dormant, dormantCap);
			}
		}
		catch
		{
			// ignore
		}

		return total;
	}

	private static int FlushLane(object syncInst, NetPlayer plr, SyncLane lane, int cap)
	{
		if (cap <= 0)
			return 0;

		object perPlr = CoolSyncReflect.GetPerPlrState.Invoke(syncInst, new object[] { plr.clientId });
		if (perPlr == null)
			return 0;

		int forceCap = plr.IsAlive() ? 100 : 20;
		int queueDepth = 0;
		if (CoolSyncReflect.PP_ForceSync.GetValue(perPlr) is Queue<knetid> fq)
			queueDepth = fq.Count;

		int flushed = 0;
		while (flushed < cap && queueDepth < forceCap)
		{
			if (!DirtyTracker.TryDequeue(plr.clientId, lane, out knetid objId))
				break;

			try
			{
				CoolSyncReflect.QueueForceSync.Invoke(syncInst, new object[] { plr.clientId, objId });
				flushed++;
				queueDepth++;
				ModTelemetry.Increment(lane == SyncLane.Hot ? "lane_hot" : lane == SyncLane.Near ? "lane_near" : "lane_dormant");
			}
			catch
			{
				break;
			}
		}

		return flushed;
	}
}
