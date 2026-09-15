using System;
using BepInEx.Logging;

namespace KrokMPOptimization2;

// centralizes plugin logging and keeps repeated hot-path errors readable.
internal static class OptLog
{
	internal static bool Verbose =>
		Plugin.VerboseLogging != null && Plugin.VerboseLogging.Value;

	internal static bool Stats =>
		(Plugin.DebugLog != null && Plugin.DebugLog.Value) || Verbose;

	internal static bool Profiler =>
		Plugin.FrameTimingEnabled != null && Plugin.FrameTimingEnabled.Value;

	internal static void Info(string message) => Plugin.Log?.LogInfo(message);

	internal static void Warn(string message) => Plugin.Log?.LogWarning(message);

	internal static void Error(string message) => Plugin.Log?.LogError(message);

	internal static void Error(string area, Exception ex) =>
		Plugin.Log?.LogError($"[{area}] {ex}");

	internal static void HotPathError(string area, Exception ex, ref int counter, ref string lastMessage)
	{
		counter++;
		string msg = ex.ToString();
		if (lastMessage != msg || counter == 1)
		{
			lastMessage = msg;
			Plugin.Log?.LogError($"[KrokMPOpt2] ERR area={area} #{counter}: {ex}");
		}
	}
}
