using UnityEngine;

namespace KrokMPOptimization2;

// records wall time for selected host and client hot paths.
internal static class FrameTiming
{
	internal static bool Active =>
		Plugin.Enabled != null && Plugin.Enabled.Value
		&& Plugin.FrameTimingEnabled != null && Plugin.FrameTimingEnabled.Value;

	internal static double Begin() => Active ? Time.realtimeSinceStartupAsDouble : 0;

	internal static void End(string bucket, double startRealTime)
	{
		if (startRealTime <= 0 || !Active)
			return;
		double ms = (Time.realtimeSinceStartupAsDouble - startRealTime) * 1000.0;
		FrameTimingWindow.AddMs(bucket, ms);
	}
}
