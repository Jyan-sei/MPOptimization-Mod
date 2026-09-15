using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace KrokMPOptimization2;

// rolling wall-time totals by bucket; flush emits averages per frame.
internal static class FrameTimingWindow
{
	private static readonly ConcurrentDictionary<string, double> MsTotal = new ConcurrentDictionary<string, double>();

	internal static void AddMs(string bucket, double ms)
	{
		if (string.IsNullOrEmpty(bucket) || ms <= 0 || ms > 5000)
			return;
		MsTotal.AddOrUpdate(bucket, ms, (_, v) => v + ms);
	}

	internal static string FormatFlush(int frameCount)
	{
		if (MsTotal.IsEmpty || frameCount <= 0)
		{
			Clear();
			return "";
		}

		var rows = new List<KeyValuePair<string, double>>(MsTotal.Count);
		foreach (var kv in MsTotal)
			rows.Add(kv);

		rows.Sort((a, b) => b.Value.CompareTo(a.Value));

		double invFrames = 1.0 / frameCount;
		var sb = new StringBuilder(256);
		sb.Append("timingAvgMs=");
		for (int i = 0; i < rows.Count; i++)
		{
			if (i > 0)
				sb.Append(',');
			double avg = rows[i].Value * invFrames;
			sb.Append(rows[i].Key).Append(':').Append(avg.ToString("F3"));
		}

		Clear();
		return sb.ToString();
	}

	internal static void Clear()
	{
		foreach (var key in MsTotal.Keys)
			MsTotal.TryRemove(key, out _);
	}
}
