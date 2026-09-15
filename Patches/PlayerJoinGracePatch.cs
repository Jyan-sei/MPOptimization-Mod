using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;

namespace KrokMPOptimization2;

[HarmonyPatch]
// tracks the temporary queue cap used while a client finishes joining.
internal static class PlayerJoinGracePatch
{
	static MethodBase TargetMethod() =>
		AccessTools.Method(CoolSyncReflect.CoolSyncType, "GetPerPlrState");

	static void Prefix(object __instance, knetid plrId, ref bool __state)
	{
		__state = false;
		if (Plugin.Enabled == null || !Plugin.Enabled.Value || Net.is_client)
			return;

		try
		{
			if (CoolSyncReflect.PerPlrStates.GetValue(__instance) is System.Collections.IDictionary states)
				__state = !states.Contains(plrId);
		}
		catch
		{
			__state = false;
		}
	}

	static void Postfix(knetid plrId, bool __state)
	{
		if (!__state)
			return;
		JoinQueueGrace.MarkJoined(plrId);
		JoinBurst.StartForPlayer(plrId);
	}
}
