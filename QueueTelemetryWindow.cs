using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace KrokMPOptimization2;

// owns the rolling queue report and its optional on-disk profiler export.
internal static class QueueTelemetryWindow
{
	private static long _queueInGlobal;
	private static long _queueDrainGlobal;
	private static long _pinTimeout;
	private static long _gateStalls;
	private static int _maxQ;

	private static double _frameMsSum;
	private static int _frameCount;
	private static int _framesOver33;
	private static double _maxFrameMs;

	private static readonly ConcurrentDictionary<string, long> QueueInPlr = new ConcurrentDictionary<string, long>();
	private static readonly ConcurrentDictionary<string, long> QueueDrainPlr = new ConcurrentDictionary<string, long>();

	internal static void RecordFrame(float unscaledDt)
	{
		if (!OptLog.Profiler && !OptLog.Stats)
			return;

		if (unscaledDt <= 0f)
			unscaledDt = 0.001f;
		if (unscaledDt > 0.5f)
			unscaledDt = 0.5f;

		double frameMs = unscaledDt * 1000.0;
		Interlocked.Increment(ref _frameCount);
		AddFrameMs(frameMs);
	}

	private static void AddFrameMs(double frameMs)
	{
		// writes happen on the main thread; flush reads counters with interlocked operations.
		double prev = Volatile.Read(ref _frameMsSum);
		Volatile.Write(ref _frameMsSum, prev + frameMs);
		if (frameMs > 33.0)
			Interlocked.Increment(ref _framesOver33);

		double curMax = Volatile.Read(ref _maxFrameMs);
		while (frameMs > curMax && Interlocked.CompareExchange(ref _maxFrameMs, frameMs, curMax) != curMax)
			curMax = Volatile.Read(ref _maxFrameMs);
	}

	internal static void RecordEnqueue(object perPlr)
	{
		Interlocked.Increment(ref _queueInGlobal);
		string key = PlayerKey(perPlr);
		if (key != null)
			QueueInPlr.AddOrUpdate(key, 1, (_, v) => v + 1);

		int q = CoolSyncQueueProbe.GetSnapshotCount(perPlr);
		int curMax = Volatile.Read(ref _maxQ);
		while (q > curMax && Interlocked.CompareExchange(ref _maxQ, q, curMax) != curMax)
			curMax = Volatile.Read(ref _maxQ);
	}

	internal static void RecordDrain(object perPlr)
	{
		Interlocked.Increment(ref _queueDrainGlobal);
		string key = PlayerKey(perPlr);
		if (key != null)
			QueueDrainPlr.AddOrUpdate(key, 1, (_, v) => v + 1);
	}

	internal static void RecordGateStall() => Interlocked.Increment(ref _gateStalls);

	internal static void RecordPinTimeout() => Interlocked.Increment(ref _pinTimeout);

	internal static void PrunePlayer(string playerKey)
	{
		if (string.IsNullOrEmpty(playerKey))
			return;
		QueueInPlr.TryRemove(playerKey, out _);
		QueueDrainPlr.TryRemove(playerKey, out _);
	}

	internal static void Flush(float windowSeconds)
	{
		if (!OptLog.Profiler && !OptLog.Stats)
		{
			ResetCounters();
			return;
		}

		if (windowSeconds < 0.1f)
			windowSeconds = 10f;

		int frames = Interlocked.Exchange(ref _frameCount, 0);
		double frameMsSum = Interlocked.Exchange(ref _frameMsSum, 0);
		int over33 = Interlocked.Exchange(ref _framesOver33, 0);
		double maxFrame = Interlocked.Exchange(ref _maxFrameMs, 0);

		float frameMsAvg = frames > 0 ? (float)(frameMsSum / frames) : 0f;
		float fpsFromAvg = frameMsAvg > 0f ? 1000f / frameMsAvg : 0f;
		float hitchPct = frames > 0 ? 100f * over33 / frames : 0f;
		long memMb = GC.GetTotalMemory(false) / (1024 * 1024);

		int maxQ = Math.Max(CoolSyncQueueProbe.SampleMaxObjectSyncQueue(), Interlocked.Exchange(ref _maxQ, 0));

		string timing = OptLog.Profiler ? FrameTimingWindow.FormatFlush(frames) : "";
		string eventAvgs = ModTelemetry.FormatEventAvgs();
		string modCounters = ModTelemetry.FormatCounters(windowSeconds);
		string queueLine = "";

		if (OptLog.Stats)
		{
			long qIn = Interlocked.Exchange(ref _queueInGlobal, 0);
			long qDrain = Interlocked.Exchange(ref _queueDrainGlobal, 0);
			long pin = Interlocked.Exchange(ref _pinTimeout, 0);
			long stalls = Interlocked.Exchange(ref _gateStalls, 0);
			double inv = 1.0 / windowSeconds;

			var sb = new StringBuilder(512);
			sb.Append($"queueInGlobal={qIn * inv:F2}/s ");
			sb.Append("queueInPlr=").Append(FormatPlrRates(QueueInPlr, inv)).Append(' ');
			sb.Append($"queueDrainGlobal={qDrain * inv:F2}/s ");
			sb.Append("queueDrainPlr=").Append(FormatPlrRates(QueueDrainPlr, inv)).Append(' ');
			sb.Append($"pinTimeoutPerSec={pin * inv:F2}/s ");
			sb.Append($"maxQ={maxQ} gateStalls={stalls}");
			queueLine = sb.ToString();

			ClearDict(QueueInPlr);
			ClearDict(QueueDrainPlr);
		}
		else
		{
			Interlocked.Exchange(ref _queueInGlobal, 0);
			Interlocked.Exchange(ref _queueDrainGlobal, 0);
			Interlocked.Exchange(ref _pinTimeout, 0);
			Interlocked.Exchange(ref _gateStalls, 0);
			ClearDict(QueueInPlr);
			ClearDict(QueueDrainPlr);
		}

		if (OptLog.Profiler)
		{
			var prof = new StringBuilder(512);
			prof.Append("[KrokMPOpt2] prof window").Append(windowSeconds.ToString("F0")).Append("s ");
			prof.Append("frameMsAvg=").Append(frameMsAvg.ToString("F1")).Append(' ');
			prof.Append("fpsFromAvg=").Append(fpsFromAvg.ToString("F1")).Append(' ');
			prof.Append("hitchPct=").Append(hitchPct.ToString("F1")).Append(' ');
			prof.Append("maxFrameMs=").Append(maxFrame.ToString("F1")).Append(' ');
			prof.Append("memMb=").Append(memMb).Append(' ');
			prof.Append("maxQ=").Append(maxQ);
			if (!string.IsNullOrEmpty(timing))
			{
				prof.Append(' ');
				prof.Append(timing);
				prof.Append(" timingNote=overlapping");
			}

			if (!string.IsNullOrEmpty(eventAvgs))
			{
				prof.Append(' ');
				prof.Append(eventAvgs);
			}

			if (!string.IsNullOrEmpty(modCounters))
			{
				prof.Append(' ');
				prof.Append(modCounters);
			}

			OptLog.Info(prof.ToString());
			ModProfilerWindow.MaybeAppendCsv(windowSeconds, frameMsAvg, fpsFromAvg, hitchPct, maxFrame, memMb, maxQ,
				timing, queueLine, eventAvgs, modCounters);
		}

		if (OptLog.Stats)
		{
			var sb = new StringBuilder(768);
			sb.Append("[KrokMPOpt2] window").Append(windowSeconds.ToString("F0")).Append("s ");
			sb.Append(queueLine).Append(' ');
			sb.Append($"frameMsAvg={frameMsAvg:F1} fpsFromAvg={fpsFromAvg:F1} hitchPct={hitchPct:F1} maxFrameMs={maxFrame:F1} memMb={memMb}");
			if (!string.IsNullOrEmpty(timing) && !OptLog.Profiler)
			{
				sb.Append(' ');
				sb.Append(timing);
			}

			if (!string.IsNullOrEmpty(eventAvgs))
			{
				sb.Append(' ');
				sb.Append(eventAvgs);
			}

			if (!string.IsNullOrEmpty(modCounters))
			{
				sb.Append(' ');
				sb.Append(modCounters);
			}

			OptLog.Info(sb.ToString());
		}

		if (!OptLog.Profiler)
			FrameTimingWindow.Clear();
		ModTelemetry.ClearEvents();
	}

	private static void ResetCounters()
	{
		Interlocked.Exchange(ref _queueInGlobal, 0);
		Interlocked.Exchange(ref _queueDrainGlobal, 0);
		Interlocked.Exchange(ref _pinTimeout, 0);
		Interlocked.Exchange(ref _gateStalls, 0);
		Interlocked.Exchange(ref _maxQ, 0);
		Interlocked.Exchange(ref _frameCount, 0);
		Interlocked.Exchange(ref _frameMsSum, 0);
		Interlocked.Exchange(ref _framesOver33, 0);
		Interlocked.Exchange(ref _maxFrameMs, 0);
		FrameTimingWindow.Clear();
		ClearDict(QueueInPlr);
		ClearDict(QueueDrainPlr);
	}

	private static void ClearDict(ConcurrentDictionary<string, long> dict)
	{
		foreach (var key in dict.Keys)
			dict.TryRemove(key, out _);
	}

	private static string FormatPlrRates(ConcurrentDictionary<string, long> dict, double inv)
	{
		if (dict.IsEmpty)
			return "";

		var sb = new StringBuilder(128);
		bool first = true;
		foreach (var kv in dict)
		{
			if (!first)
				sb.Append(',');
			first = false;
			sb.Append(kv.Key).Append(':').Append((kv.Value * inv).ToString("F2")).Append("/s");
		}
		return sb.ToString();
	}

	private static string PlayerKey(object perPlr)
	{
		if (!CoolSyncQueueProbe.TryGetPlrId(perPlr, out knetid id))
			return null;
		string s = id.ToString();
		if (s.StartsWith("STEAM_", StringComparison.OrdinalIgnoreCase))
			s = s.Substring(6);
		return s;
	}
}
