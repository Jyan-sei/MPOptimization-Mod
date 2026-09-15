using System;
using System.Collections.Generic;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace KrokMPOptimization2;

// removes registry entries that are outside the active simulation window.
internal static class RegistryCull
{
	private sealed class CullCandidate
	{
		internal knetid SyncId;
		internal float NearestPlayerDistSq;
	}

	private static float _timer;

	internal static void TickSlowMode()
	{
		if (Plugin.Enabled == null || !Plugin.Enabled.Value || !Net.is_server)
			return;

		float interval = Plugin.CullIntervalSeconds?.Value ?? 8f;
		if (interval <= 0f)
			return;

		_timer += Time.deltaTime;
		if (_timer < interval)
			return;
		_timer = 0f;

		if (NetPlayer.AllLivingPlayers == null || NetPlayer.AllLivingPlayers.Count == 0)
			return;

		InventoryHelper.TryResolve();

		float cullDist = Plugin.CullDistance?.Value ?? 240f;
		if (cullDist <= 0f && !RegistrySimWindow.UseCpuOptAuthorityCull)
			return;

		float cullDistSq = cullDist * cullDist;
		int culled = 0;
		object syncInst = CoolSyncReflect.GetObjectSyncInst();
		bool chunkCull = RegistrySimWindow.UseCpuOptAuthorityCull;

		try
		{
			var candidates = new List<CullCandidate>(64);
			var stale = new List<KeyValuePair<GameObject, SyncInfo>>(8);
			foreach (KeyValuePair<GameObject, SyncInfo> kv in NetObjectRegistry.SyncRegistry)
			{
				SyncInfo si = kv.Value;
				if (si == null || (UnityEngine.Object)(object)kv.Key == (UnityEngine.Object)null || (UnityEngine.Object)(object)si.go == (UnityEngine.Object)null)
				{
					stale.Add(kv);
					continue;
				}
				if (DirtyTracker.HasPending(si.syncId))
					continue;
				if (ShouldKeepRegistered(si.go))
					continue;

				Vector3 pos = si.go.transform.position;
				if (!RegistrySimWindow.IsCullCandidate(pos, cullDistSq, out float nearestSq))
					continue;

				candidates.Add(new CullCandidate
				{
					SyncId = si.syncId,
					NearestPlayerDistSq = nearestSq
				});
			}

					// trim the farthest objects first when the registry exceeds its target size.
			candidates.Sort((a, b) => b.NearestPlayerDistSq.CompareTo(a.NearestPlayerDistSq));

			int batch = Math.Min(32, candidates.Count);
			var toRemove = new List<knetid>(batch);
			for (int i = 0; i < batch; i++)
				toRemove.Add(candidates[i].SyncId);

			for (int i = 0; i < toRemove.Count; i++)
			{
				try
				{
					if (!CoolSyncReflect.TryServerDeleteObject(syncInst, toRemove[i]))
						continue;
					culled++;
				}
				catch
				{
					// ignore
				}
			}

			culled += DropStale(stale);
		}
		catch
		{
			// ignore
		}

		if (culled > 0)
		{
			ModTelemetry.AddRegistryCull(culled);
			if (Plugin.VerboseLogging?.Value == true)
			{
				string mode = chunkCull ? "cpuopt-chunk" : "euclidean";
				OptLog.Info($"[KrokMPOpt2] registry_cull count={culled} mode={mode}");
			}
		}

		ObjStatePrune.PruneOrphans("cull");
	}

	static int DropStale(List<KeyValuePair<GameObject, SyncInfo>> stale)
	{
		if (stale == null || stale.Count == 0)
			return 0;

		int n = 0;
		int cap = Math.Min(16, stale.Count);
		for (int i = 0; i < cap; i++)
		{
			KeyValuePair<GameObject, SyncInfo> kv = stale[i];
			try
			{
				NetObjectRegistry.SyncRegistry.Remove(kv.Key);
				if (kv.Value != null)
					NetObjectRegistry.NetIdToSyncInfoDict.Remove(kv.Value.syncId);
				n++;
			}
			catch
			{
			}
		}

		return n;
	}

	private static bool ShouldKeepRegistered(GameObject go)
	{
		if (go == null)
			return true;
		if (!go.activeInHierarchy)
			return true;
		if (InventoryHelper.IsOnPlayerBody(go) || InventoryHelper.IsSurfaceInventoryItem(go))
			return true;

		NetBody nb = go.GetComponentInParent<NetBody>();
		if (nb != null && nb.is_player)
			return true;

		Transform parent = go.transform.parent;
		if (parent != null && parent.name.StartsWith("SAVESTATE_", StringComparison.Ordinal))
			return true;

		return false;
	}
}
