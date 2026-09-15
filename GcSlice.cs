using System;
using System.Reflection;
using HarmonyLib;

namespace KrokMPOptimization2;

// applies the configured Unity incremental garbage-collection slice.
internal static class GcSlice
{
	internal static void Apply(float ms)
	{
		if (ms < 0.5f)
			ms = 0.5f;
		if (ms > 33f)
			ms = 33f;

		ulong ns = (ulong)(ms * 1_000_000f);
		try
		{
			Type t = AccessTools.TypeByName("UnityEngine.Scripting.GarbageCollector")
			         ?? Type.GetType("UnityEngine.Scripting.GarbageCollector, UnityEngine.CoreModule");
			PropertyInfo prop = t?.GetProperty(
				"incrementalTimeSliceNanoseconds",
				BindingFlags.Public | BindingFlags.Static);
			if (prop == null)
			{
				OptLog.Warn("[KrokMPOpt2] GarbageCollector.incrementalTimeSliceNanoseconds not found.");
				return;
			}

			object boxed = prop.PropertyType == typeof(ulong)
				? (object)ns
				: (object)(long)ns;
			prop.SetValue(null, boxed);
			object live = prop.GetValue(null);
			OptLog.Info($"[KrokMPOpt2] gcSliceMs={ms:F1} gcSliceNs={Convert.ToInt64(live)}");
		}
		catch (Exception ex)
		{
			OptLog.Error("GcSlice", ex);
		}
	}
}
