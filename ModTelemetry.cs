using System;
using System.Collections.Concurrent;
using System.Text;
using KrokoshaCasualtiesMP;

namespace KrokMPOptimization2;

// aggregates queue, producer, timing, and cleanup counters for the debug windows.
internal static class ModTelemetry
{
	private sealed class Acc
	{
		internal double Ms;
		internal long Count;
	}

	private static readonly ConcurrentDictionary<string, Acc> Events = new ConcurrentDictionary<string, Acc>();
	private static readonly ConcurrentDictionary<string, long> Counters = new ConcurrentDictionary<string, long>();

	private static long _capOverflow;
	private static long _capDroppedEst;
	private static long _ackDrainExtra;
	private static long _safetyNetOps;
	private static long _registryCull;
	private static long _laneFlush;

	internal static bool Active => OptLog.Profiler || OptLog.Stats;

	internal static void RecordEvent(string bucket, double ms)
	{
		if (!Active || string.IsNullOrEmpty(bucket) || ms < 0 || ms > 5000)
			return;

		Events.AddOrUpdate(
			bucket,
			_ => new Acc { Ms = ms, Count = 1 },
			(_, acc) =>
			{
				acc.Ms += ms;
				acc.Count++;
				return acc;
			});
	}

	internal static void Increment(string key, long n = 1) =>
		Counters.AddOrUpdate(key, n, (_, v) => v + n);

	internal static void RecordCapOverflow(int queueBefore, int cap, int droppedEst)
	{
		System.Threading.Interlocked.Increment(ref _capOverflow);
		System.Threading.Interlocked.Add(ref _capDroppedEst, droppedEst);
	}

	internal static void AddAckDrainStep() => System.Threading.Interlocked.Increment(ref _ackDrainExtra);
	internal static void AddSafetyNetOps(long n) => System.Threading.Interlocked.Add(ref _safetyNetOps, n);
	internal static void AddRegistryCull(long n) => System.Threading.Interlocked.Add(ref _registryCull, n);
	internal static void AddLaneFlush(long n) => System.Threading.Interlocked.Add(ref _laneFlush, n);

	internal static string FormatEventAvgs()
	{
		if (Events.IsEmpty)
			return "";

		var rows = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, Acc>>(Events.Count);
		foreach (var kv in Events)
			rows.Add(kv);

		rows.Sort((a, b) => b.Value.Ms.CompareTo(a.Value.Ms));

		var sb = new StringBuilder(384);
		sb.Append("eventAvgMs=");
		for (int i = 0; i < rows.Count; i++)
		{
			if (i > 0)
				sb.Append(',');
			Acc acc = rows[i].Value;
			double avg = acc.Count > 0 ? acc.Ms / acc.Count : 0;
			sb.Append(rows[i].Key).Append(':').Append(avg.ToString("F3"));
			sb.Append("(n=").Append(acc.Count).Append(')');
		}

		return sb.ToString();
	}

	internal static string FormatCounters(float windowSec)
	{
		if (windowSec < 0.1f)
			windowSec = 10f;
		double inv = 1.0 / windowSec;

		long overflow = System.Threading.Interlocked.Exchange(ref _capOverflow, 0);
		long dropped = System.Threading.Interlocked.Exchange(ref _capDroppedEst, 0);
		long ackExtra = System.Threading.Interlocked.Exchange(ref _ackDrainExtra, 0);
		long safety = System.Threading.Interlocked.Exchange(ref _safetyNetOps, 0);
		long cull = System.Threading.Interlocked.Exchange(ref _registryCull, 0);
		long laneFlush = System.Threading.Interlocked.Exchange(ref _laneFlush, 0);

		int syncReg = 0;
		try
		{
			syncReg = NetObjectRegistry.SyncRegistry?.Count ?? 0;
		}
		catch
		{
			// ignore
		}

		int plrCount = 0;
		try
		{
			plrCount = NetPlayer.AllLivingPlayers?.Count ?? 0;
		}
		catch
		{
			// ignore
		}

		int effCap = JoinQueueGrace.GetEffectiveCap(Plugin.QueueCap?.Value ?? 500);
		bool grace = JoinQueueGrace.IsAnyoneInGrace();

		var sb = new StringBuilder(640);
		sb.Append("ctx plr=").Append(plrCount);
		sb.Append(" syncReg=").Append(syncReg);
		sb.Append(" qCap=").Append(effCap);
		sb.Append(" joinGrace=").Append(grace ? "1" : "0");
		sb.Append(' ');

		string[] keys = new string[Counters.Count];
		Counters.Keys.CopyTo(keys, 0);
		for (int i = 0; i < keys.Length; i++)
		{
			if (Counters.TryRemove(keys[i], out long v) && v > 0)
				sb.Append(keys[i]).Append('=').Append((v * inv).ToString("F2")).Append("/s ");
		}

		sb.Append($"capOverflow={overflow * inv:F2}/s ");
		if (dropped > 0)
			sb.Append($"capDropEst={dropped * inv:F0}/s ");
		sb.Append($"ackDrainExtra={ackExtra * inv:F1}/s ");
		sb.Append($"safetyNetOps={safety * inv:F1}/s ");
		sb.Append($"registryCull={cull * inv:F2}/s ");
		sb.Append($"laneFlush={laneFlush * inv:F1}/s");
		return sb.ToString();
	}

	internal static void ClearEvents()
	{
		foreach (string key in Events.Keys)
			Events.TryRemove(key, out _);
	}
}
