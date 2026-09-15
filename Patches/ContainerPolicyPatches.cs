using System;
using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace KrokMPOptimization2;

// gives opened containers priority so their contents reach clients promptly.
[HarmonyPatch(typeof(PlayerCamera), "OpenContainer")]
internal static class ContainerPolicyPatches
{
	internal const ushort MsgId = 10251;

	private static MethodInfo _checkReach;
	private static bool _netRegistered;

	internal static void RegisterNetHandler()
	{
		if (_netRegistered || Plugin.ContainerPrioritySyncEnabled?.Value == false)
			return;

		if (!TryResolve())
			return;

		try
		{
			var register = AccessTools.Method(typeof(Net), "RegisterServerReceiver");
			if (register == null)
				return;
			register.Invoke(null, new object[]
			{
				MsgId,
				(KrokoshaScavMultiplayer.KrokoshaHandleNamedMessageDelegate)ServerReceiver_ContainerPrioritySync
			});
			_netRegistered = true;
			OptLog.Info($"[KrokMPOpt2] container priority sync registered msg={MsgId}");
		}
		catch (Exception ex)
		{
			OptLog.Error("ContainerPolicy RegisterNetHandler", ex);
		}
	}

	internal static bool TryResolve()
	{
		var itemSync = AccessTools.TypeByName("KrokoshaCasualtiesMP.ItemSync");
		_checkReach = itemSync != null
			? AccessTools.Method(itemSync, "CheckIfBodyReachThisItem", new[]
			{
				typeof(SyncInfo), typeof(Body), typeof(float), typeof(bool)
			})
			: null;
		return _checkReach != null;
	}

	static void Postfix(Container cont)
	{
		if (Plugin.ContainerPrioritySyncEnabled?.Value == false || !Net.running || cont == null)
			return;

		try
		{
			if (Net.is_server)
				ServerForceSyncContainer(cont);
			else if (Net.is_client)
				ClientRequestSync(cont);
		}
		catch (Exception ex)
		{
			OptLog.Error("OpenContainer priority sync", ex);
		}
	}

	internal static void ServerForceSyncContainer(Container cont)
	{
		if (cont == null)
			return;

		GameObject bagGo = cont.gameObject;
		NetObjectRegistry.TryGetSyncInfoOrRegister(bagGo, out SyncInfo bagSi);
		if (bagSi != null)
		{
			DirtyTracker.MarkDirtyAllPlayers(bagSi.syncId, SyncLane.Hot, SyncReason.ContainerOpen);
			NetObjectRegistry.Server_ObjectSyncSingle(bagGo);
		}

		for (int i = 0; i < cont.transform.childCount; i++)
		{
			Transform child = cont.transform.GetChild(i);
			if (child == null)
				continue;
			Item item = child.GetComponent<Item>();
			if (item == null)
				continue;

			GameObject go = child.gameObject;
			NetObjectRegistry.TryGetSyncInfoOrRegister(go, out SyncInfo si);
			if (si == null)
				continue;

			DirtyTracker.MarkDirtyAllPlayers(si.syncId, SyncLane.Hot, SyncReason.ContainerOpen);
			NetObjectRegistry.Server_ObjectSyncSingle(go);
		}

		ModTelemetry.Increment("containerOpen");
	}

	private static void ClientRequestSync(Container cont)
	{
		RegisterNetHandler();
		GameObject go = cont.gameObject;
		NetDataWriter writer = Net.CreateWriter(MsgId);
		if (NetObjectRegistry.TryGetSyncInfo(go, out SyncInfo si))
		{
			writer.Put((ushort)si.syncId);
			writer.Put("");
		}
		else
		{
			writer.Put((ushort)(knetid)(ushort)0);
			Item item = go.GetComponent<Item>();
			writer.Put(item != null ? item.id : "");
		}

		Vector2 pos = go.transform.position;
		writer.Put(pos.x);
		writer.Put(pos.y);
		Net.Client_Send(DeliveryMethod.ReliableOrdered, in writer);
	}

	private static void ServerReceiver_ContainerPrioritySync(knetid clientId, ref NetDataReader reader)
	{
		if (Plugin.ContainerPrioritySyncEnabled?.Value == false)
			return;
		if (!KrokoshaScavMultiplayer.IsNetworkActiveAndIsWorldGenerated()
		    || !NetPlayer.TryGetNetPlayerAndBodyFromClientId(clientId, out _, out Body body)
		    || body == null
		    || !body.conscious)
			return;

		MyLiteNetLibExtensions.Get(reader, out knetid syncId);
		reader.Get(out string itemId);
		reader.Get(out float posX);
		reader.Get(out float posY);
		Vector2 pos = new Vector2(posX, posY);

		Container cont = null;
		if (syncId != 0
		    && NetObjectRegistry.TryGetSyncInfo(syncId, out SyncInfo si)
		    && si.go != null)
		{
			cont = si.go.GetComponent<Container>();
			if (cont != null && !CanReach(body, si))
				cont = null;
		}

		if (cont == null && !string.IsNullOrEmpty(itemId))
		{
			Container best = null;
			float bestDist = float.MaxValue;
			foreach (var kv in NetObjectRegistry.SyncRegistry)
			{
				SyncInfo candidate = kv.Value;
				if (candidate?.go == null || !candidate.IsItem())
					continue;
				Item item = candidate.item;
				if (item == null || item.id != itemId)
					continue;
				Container c = item.container;
				if (c == null)
					continue;
				Vector2 cpos = c.transform.position;
				float dx = cpos.x - pos.x;
				float dy = cpos.y - pos.y;
				float dist = dx * dx + dy * dy;
				if (dist > 4f || dist >= bestDist)
					continue;
				if (!CanReach(body, candidate))
					continue;
				best = c;
				bestDist = dist;
			}
			cont = best;
		}

		if (cont != null)
			ServerForceSyncContainer(cont);
	}

	private static bool CanReach(Body body, SyncInfo si)
	{
		if (_checkReach == null || si == null)
			return false;
		try
		{
			return _checkReach.Invoke(null, new object[] { si, body, 20f, true }) is true;
		}
		catch
		{
			return false;
		}
	}
}
