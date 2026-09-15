using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace KrokMPOptimization2;

[HarmonyPatch]
// reanchors players pinned to an old snapshot queue head before the queue stalls.
internal static class HeadPinTimeoutPatch
{
	private static int _errCount;
	private static string _lastErr;
	private static bool _loggedFirst;
	private static PropertyInfo _syncSystemId;

	internal static int PinTimeoutErrors;

	static MethodBase TargetMethod() =>
		AccessTools.Method(CoolSyncReflect.CoolSyncType, "Update");

	static void Prefix(object __instance)
	{
		if (Plugin.Enabled == null || !Plugin.Enabled.Value || Net.is_client)
			return;
		if (Plugin.PinTimeoutSessionDisabled)
			return;
		if (__instance == null || !CoolSyncReflect.CoolSyncType.IsInstanceOfType(__instance))
			return;

		try
		{
			double t0 = FrameTiming.Begin();
			string sysBucket = PinBucketFor(__instance);
			TryEvictStaleHeads(__instance);
			FrameTiming.End(sysBucket, t0);
			FrameTiming.End("pinTimeout", t0);
		}
		catch (Exception ex)
		{
			PinTimeoutErrors++;
			OptLog.HotPathError("PinTimeout", ex, ref _errCount, ref _lastErr);
			Plugin.PinTimeoutSessionDisabled = true;
			OptLog.Warn("[KrokMPOpt2] pin timeout auto-disabled for session after error.");
		}
	}

	private static string PinBucketFor(object subsystem)
	{
		try
		{
			_syncSystemId ??= AccessTools.Property(typeof(BaseCoolSyncSubSystem), "syncsystemid");
			if (_syncSystemId?.GetValue(subsystem) is byte id)
				return $"pinTimeout{id}";
		}
		catch
		{
			// ignore
		}

		return "pinTimeoutX";
	}

	private static void TryEvictStaleHeads(object subsystem)
	{
		float maxAge = Plugin.HeadPinTimeoutSeconds?.Value ?? 10f;
		if (maxAge <= 0f)
			return;

		int maxEvict = Math.Max(1, Plugin.HeadPinMaxEvictPerPlayerPerTick?.Value ?? 2);
		if (CoolSyncReflect.PerPlrStates.GetValue(subsystem) is not IDictionary states)
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
				if (!CoolSyncReflect.TryPeekQueueHead(perPlr, out ushort head))
					break;

				double age = SnapshotAgeTracker.GetAgeSeconds(perPlr, head, now);
				if (age < maxAge)
					break;

				double evictT0 = ModTelemetry.Active ? Time.realtimeSinceStartupAsDouble : 0;
				int pinners = CoolSyncReflect.GetQueueHeadPinners(perPlr);
				int reanchored = 0;
				if (pinners > 0)
				{
					double reT0 = ModTelemetry.Active ? Time.realtimeSinceStartupAsDouble : 0;
					reanchored = QueueOps.ReanchorPinnersAtSnapshotId(perPlr, head);
					if (reT0 > 0)
					{
						double reMs = (Time.realtimeSinceStartupAsDouble - reT0) * 1000.0;
						ModTelemetry.RecordEvent("pinReanchor", reMs);
					}
				}

				if (!QueueOps.TryForceDequeueHead(perPlr, out ushort dequeued))
					break;

				SnapshotAgeTracker.Forget(perPlr, dequeued);
				QueueTelemetryWindow.RecordPinTimeout();
				evictions++;

				if (evictT0 > 0)
				{
					double evictMs = (Time.realtimeSinceStartupAsDouble - evictT0) * 1000.0;
					ModTelemetry.RecordEvent("pinEvict", evictMs);
				}

				if (!_loggedFirst && Plugin.VerboseLogging?.Value == true)
				{
					_loggedFirst = true;
					OptLog.Info("[KrokMPOpt2] first pinTimeout");
				}

				if (Plugin.VerboseLogging?.Value == true)
				{
					string plrLabel = CoolSyncQueueProbe.TryGetPlrId(perPlr, out var kid)
						? kid.ToString()
						: "?";
					OptLog.Info(
						$"[KrokMPOpt2] pinTimeout plr={plrLabel} head={dequeued} age={age:F1} pinners={pinners} reanchored={reanchored} evicted=1");
				}
			}
		}
	}
}
