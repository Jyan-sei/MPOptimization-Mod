using System;
using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;

namespace KrokMPOptimization2;

// clears additional acknowledged snapshot history without draining too much work at once.
[HarmonyPatch]
internal static class AckDrainPatch
{
	private static int _errCount;
	private static string _lastErr;

	static MethodBase TargetMethod() => CoolSyncReflect.ServerReceiveAck;

	static void Postfix(object __instance, NetPlayer plr)
	{
		if (Plugin.Enabled == null || !Plugin.Enabled.Value || Net.is_client)
			return;

		int cap = Plugin.AckDrainMaxPerReceive?.Value ?? 8;
		if (cap <= 0 || plr == null)
			return;

		double t0 = FrameTiming.Begin();
		try
		{
			object perPlr = CoolSyncReflect.GetPerPlrState.Invoke(__instance, new object[] { plr.clientId });
			if (perPlr == null)
				return;

			int queueDepth = CoolSyncReflect.QueueCount(perPlr);
			cap = Math.Min(cap, queueDepth);

			for (int i = 0; i < cap; i++)
			{
				var result = CoolSyncReflect.OneStepClear.Invoke(__instance, new[] { perPlr });
				if (result is not true)
					break;
				ModTelemetry.AddAckDrainStep();
			}
		}
		catch (Exception ex)
		{
			OptLog.HotPathError("AckDrain", ex, ref _errCount, ref _lastErr);
		}
		finally
		{
			FrameTiming.End("ackExtra", t0);
		}
	}
}
