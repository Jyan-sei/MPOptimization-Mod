using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace KrokMPOptimization2;

// renders the in-game profiler view for queue, producer, and memory counters.
internal static class ModProfilerWindow
{
	internal static void MaybeAppendCsv(float windowSec, float frameMsAvg, float fps, float hitchPct,
		double maxFrameMs, long memMb, int maxQ, string timing, string queueLine, string eventAvgs, string modCounters)
	{
		if (Plugin.FrameTimingCsvEnabled?.Value != true)
			return;

		try
		{
			string dir = Path.Combine(Application.persistentDataPath, "KrokMPOptimization2");
			Directory.CreateDirectory(dir);
			string path = Path.Combine(dir, "profiler.csv");
			bool writeHeader = !File.Exists(path);

			var sb = new StringBuilder(768);
			if (writeHeader)
				sb.Append("utc,windowSec,frameMsAvg,fps,hitchPct,maxFrameMs,memMb,maxQ,timingAvgMs,queueTelemetry,eventAvgMs,modCounters\n");

			sb.Append(DateTime.UtcNow.ToString("o")).Append(',');
			sb.Append(windowSec.ToString("F1")).Append(',');
			sb.Append(frameMsAvg.ToString("F2")).Append(',');
			sb.Append(fps.ToString("F2")).Append(',');
			sb.Append(hitchPct.ToString("F2")).Append(',');
			sb.Append(maxFrameMs.ToString("F2")).Append(',');
			sb.Append(memMb).Append(',');
			sb.Append(maxQ).Append(',');
			sb.Append(CsvEscape(timing)).Append(',');
			sb.Append(CsvEscape(queueLine)).Append(',');
			sb.Append(CsvEscape(eventAvgs)).Append(',');
			sb.Append(CsvEscape(modCounters));
			sb.Append('\n');

			File.AppendAllText(path, sb.ToString());
		}
		catch (Exception ex)
		{
			OptLog.Error("ModProfilerWindow CSV", ex);
		}
	}

	private static string CsvEscape(string value)
	{
		if (string.IsNullOrEmpty(value))
			return "";
		if (value.IndexOf('"') >= 0)
			value = value.Replace("\"", "\"\"");
		return "\"" + value + "\"";
	}
}
