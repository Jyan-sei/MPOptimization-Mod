using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace KrokMPOptimization2;

[HarmonyPatch]
// changes overflow trimming from the stock fraction to the configured fraction.
internal static class QueueOverflowPatch
{
	private static bool _loggedFirst;

	static MethodBase TargetMethod() =>
		CoolSyncReflect.SaveSnapshot;

	static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		if (Plugin.Enabled == null || !Plugin.Enabled.Value)
		{
			foreach (var ins in instructions)
				yield return ins;
			yield break;
		}

		float fraction = Plugin.QueueOverflowFraction?.Value ?? 0.1f;
		if (fraction < 0.01f)
			fraction = 0.01f;
		if (fraction > 1f)
			fraction = 1f;

		foreach (var ins in instructions)
		{
			if (ins.opcode == OpCodes.Ldc_R4 && ins.operand is float f && Math.Abs(f - 0.5f) < 0.001f)
				ins.operand = fraction;
			yield return ins;
		}
	}

	static void Postfix(object __instance, object perplr)
	{
		try
		{
			int q = CoolSyncQueueProbe.GetSnapshotCount(perplr);
			int cap = JoinQueueGrace.GetEffectiveCap(Plugin.QueueCap?.Value ?? 500);
			if (q <= cap)
				return;

			int drop = Mathf.CeilToInt(cap * (Plugin.QueueOverflowFraction?.Value ?? 0.1f));
			ModTelemetry.RecordCapOverflow(q, cap, drop);

			if (!_loggedFirst && Plugin.VerboseLogging?.Value == true)
			{
				_loggedFirst = true;
				OptLog.Info($"[KrokMPOpt2] first capOverflow dropped~={drop} queueBefore={q} cap={cap}");
			}
		}
		catch
		{
			// telemetry failure must not interrupt snapshot saving.
		}
	}
}
