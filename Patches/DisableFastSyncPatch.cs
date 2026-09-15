using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;

namespace KrokMPOptimization2;

// disables stock FastSync so the bounded SyncProducer owns host gather work.
[HarmonyPatch]
internal static class DisableFastSyncPatch
{
	private static bool _loggedOnce;

	static MethodBase TargetMethod() =>
		AccessTools.Method(CoolSyncReflect.ObjectSyncType, "Server_RunFastSync");

	static bool Prefix()
	{
		if (Plugin.Enabled == null || !Plugin.Enabled.Value || Net.is_client)
			return true;

		ModTelemetry.Increment("fastsync_skipped");
		if (!_loggedOnce)
		{
			_loggedOnce = true;
			OptLog.Info("[KrokMPOpt2] fastsync_disabled - stock Server_RunFastSync skipped.");
		}
		return false;
	}
}
