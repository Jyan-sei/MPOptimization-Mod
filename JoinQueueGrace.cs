using System.Collections.Generic;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace KrokMPOptimization2;

// while a client is joining, the host uses joinqueuecap instead of queuecap.
internal static class JoinQueueGrace
{
	private static readonly Dictionary<knetid, double> JoinedAt = new();
	private static int _lastAppliedCap = -1;
	private static bool _loggedGraceOn;
	private static bool _loggedGraceOff;

	internal static void MarkJoined(knetid plrId)
	{
		if (Plugin.JoinQueueCapGraceSeconds == null || Plugin.JoinQueueCapGraceSeconds.Value <= 0f)
			return;
		if (Plugin.JoinQueueCap == null || Plugin.JoinQueueCap.Value <= 0)
			return;

		if (!JoinedAt.ContainsKey(plrId))
		{
			JoinedAt[plrId] = Time.realtimeSinceStartupAsDouble;
			OptLog.Info($"[KrokMPOpt2] joinGrace start plr={plrId} cap={Plugin.JoinQueueCap.Value} for {Plugin.JoinQueueCapGraceSeconds.Value:F0}s");
		}
	}

	internal static void Forget(knetid plrId) => JoinedAt.Remove(plrId);

	internal static void Tick()
	{
		if (JoinedAt.Count == 0)
			return;

		float grace = Plugin.JoinQueueCapGraceSeconds?.Value ?? 0f;
		if (grace <= 0f)
		{
			JoinedAt.Clear();
			return;
		}

		double now = Time.realtimeSinceStartupAsDouble;
		var expired = new List<knetid>(4);
		foreach (var kv in JoinedAt)
		{
			if (now - kv.Value >= grace)
				expired.Add(kv.Key);
		}

		for (int i = 0; i < expired.Count; i++)
			JoinedAt.Remove(expired[i]);

		int effective = GetEffectiveCap(Plugin.QueueCap?.Value ?? 500);
		if (effective != _lastAppliedCap)
		{
			bool inGrace = IsAnyoneInGrace();
			if (inGrace && !_loggedGraceOn)
			{
				_loggedGraceOn = true;
				_loggedGraceOff = false;
			}
			else if (!inGrace && !_loggedGraceOff && _loggedGraceOn)
			{
				_loggedGraceOff = true;
				OptLog.Info($"[KrokMPOpt2] joinGrace ended - queueCap back to {Plugin.QueueCap?.Value ?? 500}");
			}

			QueueCapPatch.ApplyToAll("joinGrace");
			_lastAppliedCap = effective;
		}
	}

	internal static bool IsAnyoneInGrace()
	{
		if (JoinedAt.Count == 0)
			return false;

		float grace = Plugin.JoinQueueCapGraceSeconds?.Value ?? 0f;
		if (grace <= 0f)
			return false;

		double now = Time.realtimeSinceStartupAsDouble;
		foreach (double t in JoinedAt.Values)
		{
			if (now - t < grace)
				return true;
		}

		return false;
	}

	internal static int GetEffectiveCap(int normalCap)
	{
		int cap = normalCap < 8 ? 8 : normalCap;
		if (!IsAnyoneInGrace())
			return cap;

		int joinCap = Plugin.JoinQueueCap?.Value ?? 2000;
		if (joinCap < cap)
			joinCap = cap;
		return joinCap;
	}
}
