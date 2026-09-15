using System;
using UnityEngine;

namespace KrokMPOptimization2.ClientRelief;

// converts local frame time into the client-side AdaptiveSync scale.
internal static class LocalFpsScale
{
	private static readonly float[] RecentMs = new float[32];
	private static int _idx;
	private static int _count;
	private static float _currentScale = 1f;

	internal static float CurrentScale => _currentScale;

	internal static void RecordFrame(float unscaledDt)
	{
		if (unscaledDt <= 0f)
			unscaledDt = 0.001f;
		if (unscaledDt > 1f)
			unscaledDt = 1f;

		float ms = unscaledDt * 1000f;
		RecentMs[_idx] = ms;
		_idx = (_idx + 1) % RecentMs.Length;
		if (_count < RecentMs.Length)
			_count++;

		float target = Plugin.ClientReliefLocalFpsTargetMs?.Value ?? 33f;
		float minScale = Plugin.ClientReliefLocalFpsMinScale?.Value ?? 0.05f;

		double sum = 0;
		for (int i = 0; i < _count; i++)
			sum += RecentMs[i];

		float avgMs = (float)(sum / _count);
		float scale = avgMs > 0.01f ? target / avgMs : 1f;
		_currentScale = Mathf.Clamp(scale, minScale, 1f);
	}

	internal static void Reset()
	{
		_idx = 0;
		_count = 0;
		_currentScale = 1f;
		Array.Clear(RecentMs, 0, RecentMs.Length);
	}
}

internal class LocalFpsScaleHost : MonoBehaviour
{
	private void Update()
	{
		if (!Plugin.ClientReliefActive)
			return;

		LocalFpsScale.RecordFrame(Time.unscaledDeltaTime);
	}
}
