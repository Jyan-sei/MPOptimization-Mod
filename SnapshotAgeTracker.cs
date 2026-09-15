using System;
using System.Collections;
using System.Collections.Generic;

namespace KrokMPOptimization2;

// tracks when snapshot queue heads were first observed for timeout decisions.
internal static class SnapshotAgeTracker
{
	private static readonly Dictionary<(object PerPlr, ushort DeltaId), double> EnqueuedAt = new();

	internal static void MarkEnqueued(object perPlr, ushort deltaId, double now)
	{
		if (perPlr == null)
			return;
		var key = (perPlr, deltaId);
		if (!EnqueuedAt.ContainsKey(key))
			EnqueuedAt[key] = now;
	}

	internal static void Forget(object perPlr, ushort deltaId)
	{
		if (perPlr != null)
			EnqueuedAt.Remove((perPlr, deltaId));
	}

	internal static void ForgetAllForPlayer(object perPlr)
	{
		if (perPlr == null)
			return;
		var remove = new List<(object, ushort)>(8);
		foreach (var kv in EnqueuedAt)
		{
			if (ReferenceEquals(kv.Key.PerPlr, perPlr))
				remove.Add(kv.Key);
		}
		for (int i = 0; i < remove.Count; i++)
			EnqueuedAt.Remove(remove[i]);
	}

	internal static double GetAgeSeconds(object perPlr, ushort deltaId, double now)
	{
		var key = (perPlr, deltaId);
		if (!EnqueuedAt.TryGetValue(key, out double t))
		{
			EnqueuedAt[key] = now;
			return 0;
		}
		return now - t;
	}
}
