using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;

namespace KrokMPOptimization2;

// small reflection-backed probes for per-player CoolSync queue state.
internal static class CoolSyncQueueProbe
{
	internal static bool TryGetPlrId(object perPlrState, out knetid id)
	{
		id = default;
		if (CoolSyncReflect.PP_PlrId == null)
			return false;
		var v = CoolSyncReflect.PP_PlrId.GetValue(perPlrState);
		if (v is knetid k)
		{
			id = k;
			return true;
		}
		return false;
	}

	internal static int GetSnapshotCount(object perPlrState) =>
		CoolSyncReflect.PP_SnapshotQueue.GetValue(perPlrState) is ICollection c ? c.Count : 0;

	internal static int SampleMaxObjectSyncQueue()
	{
		int max = 0;
		try
		{
			object inst = CoolSyncReflect.ObjectInst?.GetValue(null);
			if (inst == null || CoolSyncReflect.PerPlrStates.GetValue(inst) is not IDictionary states)
				return 0;
			foreach (DictionaryEntry e in states)
			{
				if (e.Value == null)
					continue;
				int n = GetSnapshotCount(e.Value);
				if (n > max)
					max = n;
			}
		}
		catch
		{
			// ignore
		}
		return max;
	}
}
