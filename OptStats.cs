using UnityEngine;

namespace KrokMPOptimization2;

// advances the rolling queue telemetry window while the host session is active.
internal static class OptStats
{
	private static float _windowTimer;

	internal static void Tick(float unscaledDt)
	{
		if (Plugin.Enabled == null || !Plugin.Enabled.Value)
			return;

		QueueTelemetryWindow.RecordFrame(unscaledDt);

		float window = Plugin.TelemetryWindowSeconds?.Value ?? 10f;
		if (window < 1f)
			window = 1f;

		_windowTimer += unscaledDt;
		if (_windowTimer >= window)
		{
			_windowTimer = 0f;
			QueueTelemetryWindow.Flush(window);
		}
	}
}
