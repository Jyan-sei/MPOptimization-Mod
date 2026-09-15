using System.Collections.Generic;
using KrokoshaCasualtiesMP;

namespace KrokMPOptimization2;

// records object changes in per-player priority lanes before they are sent.
internal static class DirtyTracker
{
	private sealed class PlayerLanes
	{
		internal readonly Queue<knetid>[] Lanes = { new Queue<knetid>(), new Queue<knetid>(), new Queue<knetid>() };
		internal readonly HashSet<knetid> Pending = new HashSet<knetid>();
	}

	private static readonly Dictionary<knetid, PlayerLanes> ByPlayer = new Dictionary<knetid, PlayerLanes>();

	internal static void MarkDirtyAllPlayers(knetid objId, SyncLane lane, SyncReason reason)
	{
		if (!Net.is_server || objId == 0)
			return;

		try
		{
			foreach (NetPlayer plr in NetPlayer.AllLivingPlayers)
				MarkDirty(plr.clientId, objId, lane, reason);
		}
		catch
		{
			// ignore
		}
	}

	internal static void MarkDirty(knetid plrId, knetid objId, SyncLane lane, SyncReason reason)
	{
		if (!Net.is_server || plrId == 0 || objId == 0)
			return;

		if (!ByPlayer.TryGetValue(plrId, out PlayerLanes lanes))
		{
			lanes = new PlayerLanes();
			ByPlayer[plrId] = lanes;
		}

		if (!lanes.Pending.Add(objId))
			return;

		int laneIdx = (int)lane;
		if (laneIdx < 0 || laneIdx >= lanes.Lanes.Length)
			laneIdx = 0;
		lanes.Lanes[laneIdx].Enqueue(objId);
	}

	internal static bool TryDequeue(knetid plrId, SyncLane lane, out knetid objId)
	{
		objId = default;
		if (!ByPlayer.TryGetValue(plrId, out PlayerLanes lanes))
			return false;

		int laneIdx = (int)lane;
		var q = lanes.Lanes[laneIdx];
		while (q.Count > 0)
		{
			knetid id = q.Dequeue();
			lanes.Pending.Remove(id);
			if (id != 0)
			{
				objId = id;
				return true;
			}
		}
		return false;
	}

	internal static bool HasPending(knetid objId)
	{
		foreach (var kv in ByPlayer)
		{
			if (kv.Value.Pending.Contains(objId))
				return true;
		}
		return false;
	}

	internal static void ForgetPlayer(knetid plrId) => ByPlayer.Remove(plrId);

	internal static void ClearAll() => ByPlayer.Clear();
}
