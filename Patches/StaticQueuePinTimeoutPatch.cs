using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace KrokMPOptimization2;

[HarmonyPatch(typeof(CoolSyncManager), "Update")]
// prevents the static world-state queue from remaining pinned by one stale client.
internal static class StaticQueuePinTimeoutPatch
{
	private static int _errCount;
	private static string _lastErr;
	private static bool _loggedFirst;

	static void Postfix()
	{
		if (Plugin.Enabled == null || !Plugin.Enabled.Value || Net.is_client)
			return;
		if (Plugin.StaticPinTimeoutSessionDisabled)
			return;

		float maxAge = Plugin.StaticPinTimeoutSeconds?.Value ?? 10f;
		if (maxAge <= 0f)
			return;

		double t0 = FrameTiming.Begin();
		try
		{
			var all = AccessTools.Field(typeof(CoolSyncManager), "all_systems")?.GetValue(null) as IDictionary;
			if (all == null)
				return;

			foreach (DictionaryEntry e in all)
			{
				object sys = e.Value;
				if (sys == null || !CoolSyncReflect.StaticType.IsInstanceOfType(sys))
					continue;
				TryEvictStaleStaticHead(sys, maxAge);
			}
		}
		catch (Exception ex)
		{
			OptLog.HotPathError("StaticPinTimeout", ex, ref _errCount, ref _lastErr);
			Plugin.StaticPinTimeoutSessionDisabled = true;
			OptLog.Warn("[KrokMPOpt2] static pin timeout auto-disabled for session after error.");
		}
		finally
		{
			FrameTiming.End("staticPinTimeout", t0);
		}
	}

	private static void TryEvictStaleStaticHead(object subsystem, float maxAge)
	{
		int maxEvict = Math.Max(1, Plugin.HeadPinMaxEvictPerPlayerPerTick?.Value ?? 2);
		double now = Time.realtimeSinceStartupAsDouble;
		int evictions = 0;

		while (evictions < maxEvict)
		{
			if (!CoolSyncReflect.TryPeekStaticQueueHead(subsystem, out ushort head))
				break;

			double age = StaticSnapshotAgeTracker.GetAgeSeconds(subsystem, head, now);
			if (age < maxAge)
				break;

			int pinners = CoolSyncReflect.GetStaticQueueHeadPinners(subsystem);
			if (pinners > 0)
				ReanchorStaticPinners(subsystem, head);

			if (!TryForceDequeueStaticHead(subsystem, out ushort dequeued))
				break;

			StaticSnapshotAgeTracker.Forget(subsystem, dequeued);
			ModTelemetry.Increment("staticPinEvict");
			evictions++;

			if (!_loggedFirst && Plugin.VerboseLogging?.Value == true)
			{
				_loggedFirst = true;
				OptLog.Info($"[KrokMPOpt2] first staticPinTimeout head={dequeued} age={age:F1} pinners={pinners}");
			}
		}
	}

	private static int ReanchorStaticPinners(object subsystem, ushort pinnedDeltaId)
	{
		if (CoolSyncReflect.StaticPerPlrStates.GetValue(subsystem) is not IDictionary states
		    || CoolSyncReflect.SP_LastKnownId == null
		    || CoolSyncReflect.StaticDeltaCounter == null)
			return 0;

		ushort baseline = (ushort)CoolSyncReflect.StaticDeltaCounter.GetValue(subsystem);
		baseline = (ushort)(baseline - 1);
		int n = 0;

		foreach (DictionaryEntry e in states)
		{
			if (e.Value == null)
				continue;
			ushort lastKnown = (ushort)CoolSyncReflect.SP_LastKnownId.GetValue(e.Value);
			if (lastKnown != pinnedDeltaId)
				continue;
			CoolSyncReflect.SP_LastKnownId.SetValue(e.Value, baseline);
			n++;
		}

		return n;
	}

	private static bool TryForceDequeueStaticHead(object subsystem, out ushort dequeued)
	{
		dequeued = 0;
		if (CoolSyncReflect.StaticSnapshotQueue.GetValue(subsystem) is not Queue<ushort> q || q.Count == 0)
			return false;

		dequeued = q.Dequeue();
		if (CoolSyncReflect.StaticSnapshots.GetValue(subsystem) is IDictionary snaps)
			snaps.Remove(dequeued);
		return true;
	}
}

[HarmonyPatch(typeof(CoolSyncSubSystemStatic), "SaveSnapshot")]
internal static class StaticSnapshotEnqueuePatch
{
	static void Postfix(object __instance)
	{
		if (Plugin.Enabled == null || !Plugin.Enabled.Value || Net.is_client)
			return;
		if (CoolSyncReflect.StaticDeltaCounter?.GetValue(__instance) is ushort counter)
		{
			ushort id = (ushort)(counter - 1);
			StaticSnapshotAgeTracker.MarkEnqueued(__instance, id, UnityEngine.Time.realtimeSinceStartupAsDouble);
		}
	}
}

internal static class StaticSnapshotAgeTracker
{
	private static readonly Dictionary<(object Subsystem, ushort DeltaId), double> EnqueuedAt = new();

	internal static void MarkEnqueued(object subsystem, ushort deltaId, double now)
	{
		if (subsystem == null)
			return;
		var key = (subsystem, deltaId);
		if (!EnqueuedAt.ContainsKey(key))
			EnqueuedAt[key] = now;
	}

	internal static void Forget(object subsystem, ushort deltaId)
	{
		if (subsystem != null)
			EnqueuedAt.Remove((subsystem, deltaId));
	}

	internal static double GetAgeSeconds(object subsystem, ushort deltaId, double now)
	{
		var key = (subsystem, deltaId);
		if (!EnqueuedAt.TryGetValue(key, out double t))
		{
			EnqueuedAt[key] = now;
			return 0;
		}
		return now - t;
	}
}
