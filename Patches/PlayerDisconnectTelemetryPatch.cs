using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;

namespace KrokMPOptimization2;

[HarmonyPatch]
// records queue cleanup when a client disconnects.
internal static class PlayerDisconnectTelemetryPatch
{
	static MethodBase TargetMethod() =>
		AccessTools.Method(CoolSyncReflect.CoolSyncType, "Server_RemovePlr");

	static void Prefix(knetid plrId)
	{
		JoinQueueGrace.Forget(plrId);
		DirtyTracker.ForgetPlayer(plrId);
		SteamAvatarPrune.DestroyUnreferenced("serverRemovePlr");
		try
		{
			string key = plrId.ToString();
			if (key.StartsWith("STEAM_"))
				key = key.Substring(6);
			QueueTelemetryWindow.PrunePlayer(key);
		}
		catch
		{
			// ignore
		}
	}
}
