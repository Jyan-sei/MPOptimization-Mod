using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace KrokMPOptimization2;

[HarmonyPatch]
// removes force-sync entries that have blocked a client queue past the configured age.
internal static class ForceSyncQueueTimeoutPatch
{
	static MethodBase TargetMethod() =>
		CoolSyncReflect.InternalQueueForceSync;

	static void Postfix(object plrstate, knetid obj_netId)
	{
		if (Plugin.Enabled == null || !Plugin.Enabled.Value || Net.is_client)
			return;
		if (plrstate == null)
			return;

		ForceSyncAgeTracker.MarkEnqueued(plrstate, obj_netId, Time.realtimeSinceStartupAsDouble);
	}
}

[HarmonyPatch(typeof(CoolSyncSubSystemForObjects), "Update")]
internal static class ForceSyncQueueEvictPatch
{
	private static bool _loggedFirst;

	static void Postfix(object __instance)
	{
		if (Plugin.Enabled == null || !Plugin.Enabled.Value || Net.is_client)
			return;

		float maxAge = Plugin.ForceSyncHeadTimeoutSeconds?.Value ?? 10f;
		if (maxAge <= 0f)
			return;

		int maxEvict = Math.Max(1, Plugin.ForceSyncMaxEvictPerPlayerPerTick?.Value ?? 4);
		if (CoolSyncReflect.PerPlrStates.GetValue(__instance) is not IDictionary states)
			return;

		double now = Time.realtimeSinceStartupAsDouble;
		foreach (DictionaryEntry entry in states)
		{
			object perPlr = entry.Value;
			if (perPlr == null)
				continue;

			int evictions = 0;
			while (evictions < maxEvict)
			{
				if (!TryPeekForceSyncHead(perPlr, out knetid head))
					break;

				double age = ForceSyncAgeTracker.GetAgeSeconds(perPlr, head, now);
				if (age < maxAge)
					break;

				if (!TryDequeueForceSyncHead(perPlr, out knetid dequeued))
					break;

				ForceSyncAgeTracker.Forget(perPlr, dequeued);
				ModTelemetry.Increment("forceSyncTimeout");
				evictions++;

				if (!_loggedFirst && Plugin.VerboseLogging?.Value == true)
				{
					_loggedFirst = true;
					OptLog.Info($"[KrokMPOpt2] first forceSyncTimeout netId={dequeued} age={age:F1}");
				}
			}
		}
	}

	private static bool TryPeekForceSyncHead(object perPlr, out knetid head)
	{
		head = default;
		if (CoolSyncReflect.PP_ForceSync.GetValue(perPlr) is not IEnumerable queue)
			return false;
		foreach (object idObj in queue)
		{
			if (idObj is knetid kid)
			{
				head = kid;
				return true;
			}
			break;
		}
		return false;
	}

	private static bool TryDequeueForceSyncHead(object perPlr, out knetid dequeued)
	{
		dequeued = default;
		if (CoolSyncReflect.PP_ForceSync.GetValue(perPlr) is Queue<knetid> q && q.Count > 0)
		{
			dequeued = q.Dequeue();
			return true;
		}
		return false;
	}
}

internal static class ForceSyncAgeTracker
{
	private static readonly Dictionary<(object PerPlr, knetid NetId), double> EnqueuedAt = new();

	internal static void MarkEnqueued(object perPlr, knetid netId, double now)
	{
		if (perPlr == null)
			return;
		var key = (perPlr, netId);
		if (!EnqueuedAt.ContainsKey(key))
			EnqueuedAt[key] = now;
	}

	internal static void Forget(object perPlr, knetid netId)
	{
		if (perPlr != null)
			EnqueuedAt.Remove((perPlr, netId));
	}

	internal static double GetAgeSeconds(object perPlr, knetid netId, double now)
	{
		var key = (perPlr, netId);
		if (!EnqueuedAt.TryGetValue(key, out double t))
		{
			EnqueuedAt[key] = now;
			return 0;
		}
		return now - t;
	}
}
