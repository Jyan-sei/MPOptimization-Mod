using System.Collections.Generic;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace KrokMPOptimization2;

// periodically finds nearby objects missed by event-driven dirty tracking.
internal static class SafetyNetSweep
{
	private static float _timer;

	internal static void Tick(float dt)
	{
		if (Plugin.Enabled == null || !Plugin.Enabled.Value || !Net.is_server)
			return;

		float hz = Plugin.SafetyNetHz?.Value ?? 1f;
		if (hz <= 0f)
			return;

		_timer += dt;
		float interval = 1f / hz;
		if (_timer < interval)
			return;
		_timer = 0f;

		float radius = Plugin.SafetyNetRadius?.Value ?? 32f;
		int maxPerSweep = Plugin.SafetyNetMaxPerSweep?.Value ?? 16;
		if (radius <= 0f || maxPerSweep <= 0)
			return;

		double t0 = FrameTiming.Begin();
		int ops = 0;

		try
		{
			foreach (NetPlayer plr in NetPlayer.AllLivingPlayers)
			{
				if (!plr.IsAlive())
					continue;

				HashSet<GameObject> gathered = NetObjectRegistry.GatherClosestObjectsToSync(plr.pos, plr.body, radius);
				foreach (GameObject go in gathered)
				{
					if (go == null || InventoryGatherFilter.ShouldExcludeFromGather(go))
						continue;

					SyncInfo si = NetObjectRegistry.Server_EnsureItemIsNetworkRegistered(go);
					if (si == null)
						continue;

					DirtyTracker.MarkDirtyAllPlayers(si.syncId, SyncLane.Dormant, SyncReason.SafetyNet);
					ops++;
					if (ops >= maxPerSweep * NetPlayer.AllLivingPlayers.Count)
						break;
				}
			}
		}
		catch
		{
			// ignore
		}

		if (ops > 0)
			ModTelemetry.AddSafetyNetOps(ops);

		FrameTiming.End("safetyNet", t0);
	}
}
