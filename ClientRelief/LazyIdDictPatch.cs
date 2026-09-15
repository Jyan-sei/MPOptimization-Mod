using System.Text;
using HarmonyLib;
using KrokoshaCasualtiesMP;

namespace KrokMPOptimization2.ClientRelief;

// avoids rebuilding Steam player maps when the player set has not changed.
[HarmonyPatch(typeof(TransportSteamworks), "UpdateTheIDDicts")]
internal static class LazyIdDictPatch
{
	private static int _lastPlayerCount = -1;
	private static string _lastPlayerSig = "";

	static bool Prefix()
	{
		if (!Plugin.ClientReliefActive || Plugin.ClientReliefLazyIdDicts?.Value != true)
			return true;

		if (!Net.running)
		{
			_lastPlayerCount = -1;
			_lastPlayerSig = "";
			return true;
		}

		var dict = NetPlayer.ClientIdToPlayerDict;
		int count = dict?.Count ?? 0;
		string sig = BuildPlayerSig(dict);

		if (count == _lastPlayerCount && sig == _lastPlayerSig)
			return false;

		_lastPlayerCount = count;
		_lastPlayerSig = sig;
		return true;
	}

	private static string BuildPlayerSig(System.Collections.Generic.Dictionary<knetid, NetPlayer> dict)
	{
		if (dict == null || dict.Count == 0)
			return "";

		var sb = new StringBuilder(dict.Count * 12);
		foreach (var kv in dict)
		{
			sb.Append(kv.Key).Append(':').Append(kv.Value?.steam_id ?? 0).Append(';');
		}

		return sb.ToString();
	}
}
