using HarmonyLib;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace KrokMPOptimization2.ClientRelief;

// reduces registry polling on clients whose local frame time is already high.
[HarmonyPatch(typeof(NetObjectRegistry), "_ObjectSyncUpdateLoopUniversal")]
internal static class ClientRegistryThrottlePatch
{
	static bool Prefix(bool do_slow_mode)
	{
		if (!Plugin.ClientReliefActive || Plugin.ClientReliefRegistryThrottleEnabled?.Value != true)
			return true;
		if (!Net.running || !Net.is_client)
			return true;

		float scale = LocalFpsScale.CurrentScale;
		float threshold = Plugin.ClientReliefRegistryThrottleScaleThreshold?.Value ?? 0.35f;
		if (scale >= threshold)
			return true;

		if (do_slow_mode)
			return Time.frameCount % 4 == 0;

		return Time.frameCount % 2 == 0;
	}
}
