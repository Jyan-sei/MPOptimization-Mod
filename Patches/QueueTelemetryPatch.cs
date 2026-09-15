using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KrokMPOptimization2;

[HarmonyPatch]
// samples queue depth, pinning, and drain behavior for the telemetry window.
internal static class QueueTelemetryPatch
{
	static MethodBase TargetMethod() => CoolSyncReflect.SaveSnapshot;

	static void Prefix(object perplr, ref int __state)
	{
		__state = 0;
		try
		{
			if (Plugin.Enabled?.Value == true)
				__state = CoolSyncReflect.QueueCount(perplr);
		}
		catch
		{
			// ignore
		}
	}

	static void Postfix(object perplr, int __state)
	{
		if (Plugin.Enabled == null || !Plugin.Enabled.Value)
			return;

		try
		{
			int after = CoolSyncReflect.QueueCount(perplr);
			if (after > __state)
			{
				QueueTelemetryWindow.RecordEnqueue(perplr);
				ushort curDelta = (ushort)CoolSyncReflect.PP_CurDelta.GetValue(perplr);
				SnapshotAgeTracker.MarkEnqueued(perplr, curDelta, Time.realtimeSinceStartupAsDouble);
			}
		}
		catch
		{
			// hot path safe
		}
	}
}

[HarmonyPatch]
internal static class QueueDrainTelemetryPatch
{
	static MethodBase TargetMethod() => CoolSyncReflect.OneStepClear;

	static void Postfix(object plrstate, ref bool __result)
	{
		if (Plugin.Enabled == null || !Plugin.Enabled.Value)
			return;

		try
		{
			if (__result)
				QueueTelemetryWindow.RecordDrain(plrstate);
			else if (CoolSyncReflect.QueueCount(plrstate) >= 2)
				QueueTelemetryWindow.RecordGateStall();
		}
		catch
		{
			// hot path safe
		}
	}
}
