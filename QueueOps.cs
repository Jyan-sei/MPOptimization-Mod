using System;
using System.Collections;
using System.Collections.Generic;
using KrokoshaCasualtiesMP;

namespace KrokMPOptimization2;

// performs the queue mutations shared by timeout, disconnect, and flush patches.
internal static class QueueOps
{
	internal static int FlushSnapshotHistory(object perPlr)
	{
		int removed = 0;
		if (CoolSyncReflect.PP_Snapshots.GetValue(perPlr) is IDictionary snaps)
		{
			removed = snaps.Count;
			SnapshotPool.ReclaimDict(snaps);
		}

		if (CoolSyncReflect.PP_SnapshotQueue.GetValue(perPlr) is Queue<ushort> q)
			q.Clear();
		else if (CoolSyncReflect.PP_SnapshotQueue.GetValue(perPlr) is ICollection col)
		{
			while (col.Count > 0)
				TryDequeueOne(perPlr);
		}

		SnapshotAgeTracker.ForgetAllForPlayer(perPlr);
		return removed;
	}

	private static bool TryDequeueOne(object perPlr)
	{
		try
		{
			var queue = CoolSyncReflect.PP_SnapshotQueue.GetValue(perPlr);
			var dequeue = queue?.GetType().GetMethod("Dequeue");
			if (dequeue == null)
				return false;
			if (queue is ICollection col && col.Count == 0)
				return false;
			dequeue.Invoke(queue, null);
			return true;
		}
		catch
		{
			return false;
		}
	}

	internal static bool TryForceDequeueHead(object perPlr, out ushort dequeued)
	{
		dequeued = 0;
		if (!CoolSyncReflect.TryPeekQueueHead(perPlr, out ushort head))
			return false;

		var q = CoolSyncReflect.PP_SnapshotQueue.GetValue(perPlr);
		if (q is ICollection col && col.Count == 0)
			return false;

		if (q is Queue<ushort> typedQ)
			dequeued = typedQ.Dequeue();
		else
		{
			var dequeue = q?.GetType().GetMethod("Dequeue");
			if (dequeue?.Invoke(q, null) is ushort u)
				dequeued = u;
			else
				return false;
		}

		if (CoolSyncReflect.PP_Snapshots.GetValue(perPlr) is IDictionary snaps)
		{
			if (snaps.Contains(dequeued))
			{
				SnapshotPool.Return(snaps[dequeued]);
				snaps.Remove(dequeued);
			}
		}

		return true;
	}

	// treat the pinned snapshot as acknowledged by moving only the last-known id.
	internal static int ReanchorPinnersAtSnapshotId(object perPlr, ushort pinnedDeltaId)
	{
		if (CoolSyncReflect.PP_ObjStates.GetValue(perPlr) is not IDictionary objstates)
			return 0;

		ushort baselineId = (ushort)CoolSyncReflect.PP_CurDelta.GetValue(perPlr);
		baselineId = (ushort)(baselineId - 1);
		int n = 0;

		foreach (DictionaryEntry e in objstates)
		{
			if (e.Value == null)
				continue;
			ushort lastKnown = (ushort)CoolSyncReflect.OS_LastKnownId.GetValue(e.Value);
			if (lastKnown != pinnedDeltaId)
				continue;
			CoolSyncReflect.OS_LastKnownId.SetValue(e.Value, baselineId);
			n++;
		}

		return n;
	}

	internal static int ReanchorInventoryOnly(object subsystem, object perPlr)
	{
		if (CoolSyncReflect.ServerObjects.GetValue(subsystem) is not IDictionary live)
			return 0;

		ushort baselineId = (ushort)CoolSyncReflect.PP_CurDelta.GetValue(perPlr);
		baselineId = (ushort)(baselineId - 1);
		int n = 0;

		foreach (DictionaryEntry e in live)
		{
			if (e.Key is not knetid netId)
				continue;
			if (!InventoryHelper.IsInventoryItem(netId))
				continue;

			object objState = CoolSyncReflect.GetObjState.Invoke(perPlr, new object[] { netId });
			if (objState == null)
				continue;
			CoolSyncReflect.OS_LastKnownId.SetValue(objState, baselineId);
			n++;
		}

		return n;
	}

	internal static void ClearForceSyncQueue(object perPlr)
	{
		if (CoolSyncReflect.PP_ForceSync.GetValue(perPlr) is Queue<knetid> fq)
			fq.Clear();
	}
}
