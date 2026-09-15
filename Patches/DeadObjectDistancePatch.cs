using HarmonyLib;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace KrokMPOptimization2;

/// <summary>
/// stock Server_ObjectUpdateDistanceChecks reads si.go.transform with no null check.
/// destroyed registry entries then throw every host Update.
/// </summary>
[HarmonyPatch(typeof(NetObjectRegistry), nameof(NetObjectRegistry.Server_ObjectUpdateDistanceChecks))]
// skips distance checks for registry entries whose Unity object is already gone.
internal static class DeadObjectDistancePatch
{
	static bool Prefix(SyncInfo si, ref float __result)
	{
		if (si != null && (UnityEngine.Object)(object)si.go != (UnityEngine.Object)null && (UnityEngine.Object)(object)si.tracker != (UnityEngine.Object)null)
		{
			return true;
		}

		__result = 99999f;
		return false;
	}
}
