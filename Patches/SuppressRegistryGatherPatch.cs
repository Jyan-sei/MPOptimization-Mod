using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;

namespace KrokMPOptimization2;

[HarmonyPatch(typeof(NetObjectRegistry), "_ObjectSyncUpdateLoopUniversal")]
// suppresses the fast registry gather replaced by the host producer and cull pass.
internal static class SuppressRegistryGatherPatch
{
	static bool Prefix(bool do_slow_mode)
	{
		if (Plugin.Enabled == null || !Plugin.Enabled.Value || Net.is_client)
			return true;

		if (!do_slow_mode)
		{
			ModTelemetry.Increment("registryFastSkipped");
			return false;
		}

		RegistryCull.TickSlowMode();
		return true;
	}
}
