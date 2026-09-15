using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;

namespace KrokMPOptimization2;

[HarmonyPatch]
// applies bounded snapshot queue sizes to every resolved CoolSync subsystem.
internal static class QueueCapPatch
{
	private static bool _loggedOnce;

	static MethodBase TargetMethod() =>
		AccessTools.Method(typeof(CoolSyncManager), "OnTransportStart");

	static void Postfix() => ApplyToAll("OnTransportStart");

	internal static void ApplyToAll(string reason)
	{
		if (Plugin.Enabled == null || !Plugin.Enabled.Value)
			return;

		int cap = JoinQueueGrace.GetEffectiveCap(Plugin.QueueCap?.Value ?? 500);

		try
		{
			var all = AccessTools.Field(typeof(CoolSyncManager), "all_systems")?.GetValue(null) as IDictionary;
			if (all == null)
				return;

			int objectFamily = 0;
			int staticFamily = 0;
			foreach (DictionaryEntry e in all)
			{
				object sys = e.Value;
				if (sys == null)
					continue;

				if (CoolSyncReflect.CoolSyncType.IsInstanceOfType(sys))
				{
					CoolSyncReflect.MaxSnapshotQueue.SetValue(sys, cap);
					objectFamily++;
				}
				else if (CoolSyncReflect.StaticType.IsInstanceOfType(sys))
				{
					CoolSyncReflect.StaticMaxSnapshotQueue.SetValue(sys, cap);
					staticFamily++;
				}
			}

			if ((objectFamily + staticFamily) > 0 && !_loggedOnce)
			{
				_loggedOnce = true;
				OptLog.Info($"[KrokMPOpt2] queueCap={cap} objectSys={objectFamily} staticSys={staticFamily}");
			}
		}
		catch (Exception ex)
		{
			OptLog.Error("QueueCapPatch", ex);
		}
	}
}
