using System;
using System.Collections.Generic;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace KrokMPOptimization2;

// seeds a bounded set of nearby objects for a client during the join burst.
internal static class JoinBurst
{
	private static readonly Dictionary<knetid, int> Remaining = new Dictionary<knetid, int>();

	internal static void StartForPlayer(knetid plrId)
	{
		if (!Net.is_server || plrId == 0)
			return;

		int max = Plugin.JoinBurstMax?.Value ?? 200;
		if (max <= 0)
			return;

		Remaining[plrId] = max;
		OptLog.Info($"[KrokMPOpt2] joinBurst start plr={plrId} max={max}");
	}

	internal static void Tick()
	{
		if (Remaining.Count == 0 || !Net.is_server)
			return;

		float radius = Math.Max(Plugin.SafetyNetRadius?.Value ?? 32f, 48f);
		var done = new List<knetid>(4);
		var ids = new List<knetid>(Remaining.Keys);

		foreach (knetid plrId in ids)
		{
			if (!Remaining.TryGetValue(plrId, out int left))
				continue;
			if (left <= 0)
			{
				done.Add(plrId);
				continue;
			}

			if (!NetPlayer.TryGetNetPlayerAndNetBodyFromClientId(plrId, out NetPlayer plr, out _))
			{
				done.Add(plrId);
				continue;
			}

			int batch = Math.Min(left, 24);
			int seeded = 0;

			try
			{
				HashSet<GameObject> gathered = NetObjectRegistry.GatherClosestObjectsToSync(plr.pos, plr.body, radius);
				foreach (GameObject go in gathered)
				{
					if (go == null || InventoryGatherFilter.ShouldExcludeFromGather(go))
						continue;

					SyncInfo si = NetObjectRegistry.Server_EnsureItemIsNetworkRegistered(go);
					if (si == null)
						continue;

					DirtyTracker.MarkDirty(plrId, si.syncId, SyncLane.Hot, SyncReason.JoinBurst);
					DirtyTracker.MarkDirty(plrId, si.syncId, SyncLane.Near, SyncReason.JoinBurst);
					seeded++;
					if (seeded >= batch)
						break;
				}
			}
			catch
			{
				// ignore
			}

			left -= seeded;
			if (left <= 0 || seeded == 0)
				done.Add(plrId);
			else
				Remaining[plrId] = left;
		}

		for (int i = 0; i < done.Count; i++)
		{
			if (Remaining.Remove(done[i]))
				ModTelemetry.Increment("joinBurst");
		}
	}
}
