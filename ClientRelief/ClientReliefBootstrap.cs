using System;
using HarmonyLib;

namespace KrokMPOptimization2.ClientRelief;

// registers the client-side transport, registry, and local frame-time patches.
internal static class ClientReliefBootstrap
{
	internal const string StandaloneGuid = "com.local.krokmp.clientrelief";

	internal static void Register(Harmony harmony)
	{
		foreach (var t in new[]
		{
			typeof(TransportPollPatch),
			typeof(LazyIdDictPatch),
			typeof(LocalFpsScalePatch),
			typeof(ClientRegistryThrottlePatch),
		})
		{
			try
			{
				harmony.PatchAll(t);
			}
			catch (Exception ex)
			{
				OptLog.Warn($"[KrokMPOpt2] client relief patch skipped {t.Name}: {ex.Message}");
			}
		}

		OptLog.Info(
			$"[KrokMPOpt2] client relief armed (msg cap={Plugin.ClientReliefMaxMessagesPerFrame?.Value ?? 256}).");
	}
}
