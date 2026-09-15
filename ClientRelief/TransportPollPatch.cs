using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using Steamworks;

namespace KrokMPOptimization2.ClientRelief;

// reuses the Steam receive buffer and limits packet work performed per frame.
[HarmonyPatch(typeof(TransportSteamworks), "_Update_PollMessages")]
internal static class TransportPollPatch
{
	private static FieldInfo _connectionMapping;
	private static FieldInfo _isSteamServer;
	private static FieldInfo _connConnection;
	private static MethodInfo _onReceive;
	private static IntPtr[] _buffer;
	private static readonly List<object> _connScratch = new List<object>(16);
	private static bool _resolved;

	private static void EnsureReflection()
	{
		if (_resolved)
			return;
		_resolved = true;

		_connectionMapping = AccessTools.Field(typeof(TransportSteamworks), "connectionMapping");
		_isSteamServer = AccessTools.Field(typeof(TransportSteamworks), "is_steamserver");
		var connType = AccessTools.Inner(typeof(TransportSteamworks), "SteamConnectionData");
		_connConnection = connType != null ? AccessTools.Field(connType, "connection") : null;
		_onReceive = AccessTools.Method(typeof(TransportSteamworks), "_K_OnReceiveNetworkingMessage");
	}

	private static IntPtr[] GetBuffer(int size)
	{
		size = Math.Max(64, size);
		if (_buffer == null || _buffer.Length != size)
			_buffer = new IntPtr[size];
		return _buffer;
	}

	static bool Prefix(TransportSteamworks __instance)
	{
		if (!Plugin.ClientReliefActive)
			return true;

		EnsureReflection();
		if (_connectionMapping == null || _connConnection == null || _onReceive == null)
			return true;

		if (_connectionMapping.GetValue(__instance) is not IDictionary mapping || mapping.Count == 0)
			return false;

		bool isServer = _isSteamServer != null && (bool)_isSteamServer.GetValue(__instance);

		int bufCap = Net.is_client
			? Math.Max(64, Plugin.ClientReliefPollBufferSize?.Value ?? 512)
			: 255;
		IntPtr[] array = GetBuffer(bufCap);

		int remaining = Net.is_client
			? Math.Max(32, Plugin.ClientReliefMaxMessagesPerFrame?.Value ?? 256)
			: bufCap;

		_connScratch.Clear();
		foreach (DictionaryEntry entry in mapping)
		{
			if (entry.Value != null)
				_connScratch.Add(entry.Value);
		}

		for (int c = 0; c < _connScratch.Count; c++)
		{
			if (Net.is_client && remaining <= 0)
				break;

			object connData = _connScratch[c];
			object connVal = _connConnection.GetValue(connData);
			if (connVal == null)
				continue;

			var connection = (HSteamNetConnection)connVal;
			int num2 = isServer
				? SteamGameServerNetworkingSockets.ReceiveMessagesOnConnection(connection, array, array.Length)
				: SteamNetworkingSockets.ReceiveMessagesOnConnection(connection, array, array.Length);

			int toProcess = Net.is_client ? Math.Min(num2, remaining) : num2;
			for (int i = 0; i < num2; i++)
			{
				SteamNetworkingMessage_t msg = Marshal.PtrToStructure<SteamNetworkingMessage_t>(array[i]);
				if (i >= toProcess)
				{
					SteamNetworkingMessage_t.Release(array[i]);
					continue;
				}

				byte[] payload = new byte[msg.m_cbSize - 1];
				Marshal.Copy(msg.m_pData, payload, 0, msg.m_cbSize - 1);
				SteamNetworkingMessage_t.Release(array[i]);
				try
				{
					_onReceive.Invoke(__instance, new[] { connData, payload });
				}
				catch (Exception ex)
				{
					OptLog.Error(
						$"{__instance.GetType().Name}._Update_PollMessages client relief dispatch failed: {ex}");
				}
			}

			if (Net.is_client)
				remaining -= toProcess;
		}

		return false;
	}
}
