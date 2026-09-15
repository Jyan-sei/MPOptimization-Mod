using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using UnityEngine;
using UnityEngine.Profiling;

namespace KrokMPOptimization2;

// samples Unity, Steam, and CoolSync memory counters without allocating a report object.
internal static class HeapProbe
{
	private static float _probeTimer;
	private static float _pruneTimer;
	private static Type _tmpText;
	private static bool _tmpResolved;
	private static FieldInfo _plrInst;
	private static bool _plrInstResolved;

	internal static void Tick(float unscaledDt)
	{
		if (Plugin.HeapProbeSeconds == null)
			return;

		KeyCode key = Plugin.HeapProbeKey?.Value ?? KeyCode.F9;
		if (key != KeyCode.None && Input.GetKeyDown(key))
			Run("key", Plugin.HeapProbeForceCollect != null && Plugin.HeapProbeForceCollect.Value);

		float interval = Plugin.HeapProbeSeconds.Value;
		if (interval > 0f)
		{
			_probeTimer += unscaledDt;
			if (_probeTimer >= interval)
			{
				_probeTimer = 0f;
				Run("tick", forceCollect: false);
			}
		}

		float pruneSec = Plugin.ObjStatePruneTickSeconds?.Value ?? 30f;
		if (pruneSec > 0f && Net.is_server)
		{
			_pruneTimer += unscaledDt;
			if (_pruneTimer >= pruneSec)
			{
				_pruneTimer = 0f;
				ObjStatePrune.PruneOrphans("tick");
			}
		}
	}

	internal static void Run(string reason, bool forceCollect)
	{
		int tex = SafeCount(typeof(Texture2D));
		int mat = SafeCount(typeof(Material));
		int tmp = SafeCount(TmpTextType());
		int steam = 0;
		try
		{
			steam = KSteam.SteamImagesCache != null ? KSteam.SteamImagesCache.Count : 0;
		}
		catch
		{
			// ignore
		}

		CountCoolSync(out int snaps, out int objstates);
		int pool = SnapshotPool.PooledCount;
		long monoUsed = SafeBytes(Profiler.GetMonoUsedSizeLong);
		long monoHeap = SafeBytes(Profiler.GetMonoHeapSizeLong);

		OptLog.Info(
			$"[KrokMPOpt2] heapProbe reason={reason} tex={tex} mat={mat} tmp={tmp} steamCache={steam} " +
			$"snaps={snaps} objstates={objstates} pool={pool} " +
			$"monoUsed={Mb(monoUsed)} monoHeap={Mb(monoHeap)}");

		if (!forceCollect)
			return;

		long before = SafeBytes(Profiler.GetMonoUsedSizeLong);
		GC.Collect();
		long after = SafeBytes(Profiler.GetMonoUsedSizeLong);
		OptLog.Info(
			$"[KrokMPOpt2] heapProbe collect monoUsedBefore={Mb(before)} monoUsedAfter={Mb(after)}");
	}

	private static void CountCoolSync(out int snaps, out int objstates)
	{
		snaps = 0;
		objstates = 0;
		if (!CoolSyncReflect.Ok && !CoolSyncReflect.TryResolve())
			return;

		AddSubsystem(CoolSyncReflect.GetObjectSyncInst(), ref snaps, ref objstates);
		AddSubsystem(CoolSyncReflect.CharInst?.GetValue(null), ref snaps, ref objstates);
		if (!_plrInstResolved)
		{
			_plrInstResolved = true;
			_plrInst = AccessTools.Field(AccessTools.TypeByName("KrokoshaCasualtiesMP.PlrSync"), "inst");
		}

		AddSubsystem(_plrInst?.GetValue(null), ref snaps, ref objstates);
	}

	private static void AddSubsystem(object subsystem, ref int snaps, ref int objstates)
	{
		if (subsystem == null || CoolSyncReflect.PerPlrStates == null)
			return;
		try
		{
			if (CoolSyncReflect.PerPlrStates.GetValue(subsystem) is not IDictionary states)
				return;
			foreach (DictionaryEntry pe in states)
			{
				if (pe.Value == null)
					continue;
				if (CoolSyncReflect.PP_Snapshots?.GetValue(pe.Value) is IDictionary snapDict)
					snaps += snapDict.Count;
				if (CoolSyncReflect.PP_ObjStates?.GetValue(pe.Value) is IDictionary objDict)
					objstates += objDict.Count;
			}
		}
		catch
		{
			// ignore
		}
	}

	private static Type TmpTextType()
	{
		if (_tmpResolved)
			return _tmpText;
		_tmpResolved = true;
		_tmpText = AccessTools.TypeByName("TMPro.TMP_Text")
		           ?? AccessTools.TypeByName("TMPro.TextMeshProUGUI")
		           ?? AccessTools.TypeByName("TMPro.TextMeshPro");
		return _tmpText;
	}

	private static int SafeCount(Type t)
	{
		if (t == null)
			return 0;
		try
		{
			return Resources.FindObjectsOfTypeAll(t).Length;
		}
		catch
		{
			return 0;
		}
	}

	private static long SafeBytes(Func<long> get)
	{
		try
		{
			return get();
		}
		catch
		{
			return -1;
		}
	}

	private static long Mb(long bytes) =>
		bytes < 0 ? -1 : bytes / (1024 * 1024);
}
