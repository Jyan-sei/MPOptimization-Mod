using HarmonyLib;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace KrokMPOptimization2.ClientRelief;

// applies the local frame-time scale while the client updates its sync timers.
[HarmonyPatch(typeof(ClientMain), "Update")]
internal static class LocalFpsScalePatch
{
	static void Postfix()
	{
		if (!Plugin.ClientReliefActive || Plugin.ClientReliefLocalFpsScaleEnabled?.Value != true)
			return;
		if (!Net.running || !Net.is_client)
			return;
		if (Net.is_connecting || ClientMain.server_is_generating_world)
			return;

		float hostScale = ClientMain.ServerPerformanceScale;
		float localScale = LocalFpsScale.CurrentScale;
		float combined = Mathf.Min(hostScale, localScale);
		ClientMain.ServerPerformanceScale = combined;
		ClientMain.AdaptiveSyncTimerDelta = Time.unscaledDeltaTime * combined;
	}
}
