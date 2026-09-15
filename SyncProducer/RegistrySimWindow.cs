using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace KrokMPOptimization2;

// reports chunk membership for registry culling when CPUOptimization is present.
internal static class RegistrySimWindow
{
	private static MethodInfo _cpuOptIsInSim;
	private static MethodInfo _cpuOptIsActive;
	private static bool _cpuOptResolved;
	private static bool _cpuOptOk;

	// true when CPUOptimization chunk simulation is loaded and active.
	internal static bool UseCpuOptAuthorityCull => TryCpuOptAuthorityActive();

	// true when an object is outside the registry window, using chunks or distance.
	internal static bool IsCullCandidate(Vector3 worldPos, float cullDistSq, out float nearestPlayerDistSq)
	{
		nearestPlayerDistSq = ComputeNearestLivingPlayerDistSq(worldPos);

		if (TryCpuOptAuthorityActive())
			return !IsAuthoritySimWorldPos(worldPos);

		return nearestPlayerDistSq > cullDistSq;
	}

	internal static float ComputeNearestLivingPlayerDistSq(Vector3 worldPos)
	{
		float nearestSq = float.MaxValue;
		if (NetPlayer.AllLivingPlayers == null)
			return nearestSq;

		for (int i = 0; i < NetPlayer.AllLivingPlayers.Count; i++)
		{
			NetPlayer plr = NetPlayer.AllLivingPlayers[i];
			if (plr == null)
				continue;

			float dx = plr.pos.x - worldPos.x;
			float dy = plr.pos.y - worldPos.y;
			float d2 = dx * dx + dy * dy;
			if (d2 < nearestSq)
				nearestSq = d2;
		}

		return nearestSq;
	}

	private static bool TryCpuOptAuthorityActive()
	{
		ResolveCpuOpt();
		if (!_cpuOptOk)
			return false;

		try
		{
			return _cpuOptIsActive.Invoke(null, null) is true;
		}
		catch
		{
			return false;
		}
	}

	private static bool IsAuthoritySimWorldPos(Vector3 worldPos)
	{
		ResolveCpuOpt();
		if (!_cpuOptOk)
			return true;

		try
		{
			return _cpuOptIsInSim.Invoke(null, new object[] { worldPos }) is true;
		}
		catch
		{
			return true;
		}
	}

	private static void ResolveCpuOpt()
	{
		if (_cpuOptResolved)
			return;

		_cpuOptResolved = true;
		var t = AccessTools.TypeByName("CPUOptimization.ChunkSimQuery");
		if (t != null)
		{
			_cpuOptIsInSim = AccessTools.Method(t, "IsAuthoritySimWorldPos", new[] { typeof(Vector3) });
			_cpuOptIsActive = AccessTools.Property(t, "IsChunkSimActive")?.GetGetMethod()
			                  ?? AccessTools.Method(t, "get_IsChunkSimActive");
		}

		_cpuOptOk = _cpuOptIsInSim != null && _cpuOptIsActive != null;
	}
}
