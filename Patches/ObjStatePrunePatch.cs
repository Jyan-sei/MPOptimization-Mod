using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;

namespace KrokMPOptimization2;

// removes per-player object state for objects no longer present in the live registry.
internal static class ObjStatePrune
{
	internal static int PruneOrphans(string reason)
	{
		if (Plugin.PruneCoolSyncObjStates == null || !Plugin.PruneCoolSyncObjStates.Value)
			return 0;
		if (!CoolSyncReflect.Ok && !CoolSyncReflect.TryResolve())
			return 0;

		int n = 0;
		n += PruneSubsystem(CoolSyncReflect.GetObjectSyncInst());
		n += PruneSubsystem(CoolSyncReflect.CharInst?.GetValue(null));
		if (n > 0)
			MemoryTelemetry.EventObjstatePrune(n, reason);
		return n;
	}

	internal static int WipeAll(string reason)
	{
		if (Plugin.PruneCoolSyncObjStates == null || !Plugin.PruneCoolSyncObjStates.Value)
			return 0;
		if (!CoolSyncReflect.Ok && !CoolSyncReflect.TryResolve())
			return 0;

		int n = 0;
		n += WipeSubsystem(CoolSyncReflect.GetObjectSyncInst());
		n += WipeSubsystem(CoolSyncReflect.CharInst?.GetValue(null));
		if (n > 0)
			MemoryTelemetry.EventObjstatePrune(n, reason);
		return n;
	}

	private static int PruneSubsystem(object subsystem)
	{
		if (subsystem == null)
			return 0;
		if (CoolSyncReflect.ServerObjects.GetValue(subsystem) is not IDictionary live)
			return 0;
		if (CoolSyncReflect.PerPlrStates.GetValue(subsystem) is not IDictionary states)
			return 0;

		int removed = 0;
		var doomed = new List<object>(32);
		foreach (DictionaryEntry pe in states)
		{
			if (pe.Value == null)
				continue;
			if (CoolSyncReflect.PP_ObjStates.GetValue(pe.Value) is not IDictionary objstates)
				continue;

			doomed.Clear();
			foreach (DictionaryEntry oe in objstates)
			{
				if (!live.Contains(oe.Key))
					doomed.Add(oe.Key);
			}

			for (int i = 0; i < doomed.Count; i++)
			{
				object key = doomed[i];
				object st = objstates[key];
				if (st != null && CoolSyncReflect.OS_LastKnown != null)
					CoolSyncReflect.OS_LastKnown.SetValue(st, null);
				objstates.Remove(key);
				removed++;
			}
		}

		return removed;
	}

	private static int WipeSubsystem(object subsystem)
	{
		if (subsystem == null)
			return 0;
		if (CoolSyncReflect.PerPlrStates.GetValue(subsystem) is not IDictionary states)
			return 0;

		int n = 0;
		foreach (DictionaryEntry pe in states)
		{
			if (pe.Value == null)
				continue;
			if (CoolSyncReflect.PP_ObjStates.GetValue(pe.Value) is not IDictionary objstates)
				continue;
			foreach (DictionaryEntry oe in objstates)
			{
				if (oe.Value != null && CoolSyncReflect.OS_LastKnown != null)
					CoolSyncReflect.OS_LastKnown.SetValue(oe.Value, null);
			}
			n += objstates.Count;
			objstates.Clear();
		}

		return n;
	}
}

[HarmonyPatch]
internal static class ObjStatePruneLayerFlushPatch
{
	static MethodBase TargetMethod()
	{
		var t = AccessTools.TypeByName("KrokoshaCasualtiesMP.RegenerateWorldPatch");
		return AccessTools.Method(t, "Prefix");
	}

	static void Postfix()
	{
		if (!Net.is_server)
			return;
		ObjStatePrune.PruneOrphans("layerFlush");
	}
}
