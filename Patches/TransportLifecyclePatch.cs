using HarmonyLib;
using KrokoshaCasualtiesMP;

namespace KrokMPOptimization2;

[HarmonyPatch(typeof(CoolSyncManager), "OnTransportStart")]
// arms and resets host-only queue and producer state around MP transport lifetime.
internal static class TransportLifecyclePatch
{
	private static bool _loggedHostStart;
	private static bool _loggedClientJoin;

	static void Postfix()
	{
		bool mainEnabled = Plugin.Enabled != null && Plugin.Enabled.Value;
		bool hiFiEnabled = Plugin.ElderHiFiEnabled != null && Plugin.ElderHiFiEnabled.Value;

		if (!mainEnabled && !hiFiEnabled)
			return;

		if (Net.is_server)
		{
			if (mainEnabled)
			{
				QueueCapPatch.ApplyToAll("OnTransportStart");
				ContainerPolicyPatches.RegisterNetHandler();
				DirtyTracker.ClearAll();
			}

			if (hiFiEnabled)
				ElderHiFiSync.EnsureTickHost();

			if (!_loggedHostStart)
			{
				_loggedHostStart = true;
				_loggedClientJoin = false;
				if (mainEnabled)
					OptLog.Info(
						$"[KrokMPOpt2] host transport active - FastSync stripped, SyncProducer engaged (v{PluginInfo.Version}).");
			}
		}
		else if (Net.is_client && !_loggedClientJoin)
		{
			_loggedClientJoin = true;
			_loggedHostStart = false;
			OptLog.Info("[KrokMPOpt2] joined as client - host-only producer inactive on this machine.");
		}
	}
}

[HarmonyPatch(typeof(CoolSyncManager), "OnTransportEnd")]
internal static class TransportEndPatch
{
	static void Postfix()
	{
		bool mainEnabled = Plugin.Enabled != null && Plugin.Enabled.Value;
		if (mainEnabled)
			DirtyTracker.ClearAll();
		ElderHiFiSync.ClearElderCache();
	}
}
