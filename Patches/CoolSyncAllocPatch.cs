using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using LiteNetLib.Utils;
using UnityEngine;

namespace KrokMPOptimization2;

using ServerPerPlr = CoolSyncSubSystemForObjects.Server_PerPlrState;
using ServerSnapshot = CoolSyncSubSystemForObjects.Server_PerPlrState.Server_Snapshot;

// replaces recurring CoolSync collection and snapshot allocations with reusable buffers.
internal static class CoolSyncAlloc
{
	internal static bool Enabled =>
		Plugin.CoolSyncAllocPooling == null || Plugin.CoolSyncAllocPooling.Value;

	internal static bool WriterEnabled =>
		Plugin.CoolSyncWriterPool == null || Plugin.CoolSyncWriterPool.Value;

	private static readonly List<knetid> KeysScratch = new List<knetid>(32);
	private static IList _clientScratch;
	private static byte[] _bitsetScratch = new byte[32];
	private static readonly byte[][] ExactBitsets = new byte[65][];
	private static NetDataWriter _packWriter;

	internal static IList FillList(IEnumerable values, IList dest)
	{
		if (dest == null)
			return values is ICollection c ? new ArrayList(c) : new ArrayList();
		dest.Clear();
		if (values != null)
		{
			foreach (object v in values)
				dest.Add(v);
		}

		MemoryTelemetry.HitCoolListFill();
		return dest;
	}

	internal static IList FillClientObjectList(IEnumerable values)
	{
		if (_clientScratch == null)
		{
			Type elem = AccessTools.Inner(typeof(CoolSyncSubSystemForObjects), "Client_Object");
			_clientScratch = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elem), 256);
		}

		return FillList(values, _clientScratch);
	}

	internal static List<knetid> FillKnetidKeys(IEnumerable<knetid> keys)
	{
		KeysScratch.Clear();
		if (keys != null)
		{
			foreach (knetid id in keys)
				KeysScratch.Add(id);
		}

		MemoryTelemetry.HitCoolListFill();
		return KeysScratch;
	}

	internal static byte[] FillBitset(List<bool> bits)
	{
		int len = (bits.Count + 7) / 8;
		if (_bitsetScratch.Length < len)
			_bitsetScratch = new byte[Math.Max(len, _bitsetScratch.Length * 2)];
		else
			Array.Clear(_bitsetScratch, 0, len);

		for (int i = 0; i < bits.Count; i++)
		{
			if (bits[i])
				_bitsetScratch[i / 8] |= (byte)(1 << (i % 8));
		}

		if (len <= 64)
		{
			byte[] exact = ExactBitsets[len];
			if (exact == null)
			{
				exact = new byte[len];
				ExactBitsets[len] = exact;
			}
			else if (len > 0)
				Array.Clear(exact, 0, len);

			if (len > 0)
				Buffer.BlockCopy(_bitsetScratch, 0, exact, 0, len);
			MemoryTelemetry.HitBitsetPool();
			return exact;
		}

		var copy = new byte[len];
		if (len > 0)
			Buffer.BlockCopy(_bitsetScratch, 0, copy, 0, len);
		MemoryTelemetry.HitBitsetPool();
		return copy;
	}

	internal static List<CodeInstruction> RewriteToListCalls(List<CodeInstruction> codes)
	{
		MethodInfo fillDest = AccessTools.Method(typeof(CoolSyncAlloc), nameof(FillList));
		MethodInfo fillClient = AccessTools.Method(typeof(CoolSyncAlloc), nameof(FillClientObjectList));
		int replaced = 0;

		for (int i = 0; i < codes.Count; i++)
		{
			if (!IsToList(codes[i], out MethodInfo toList))
				continue;

			Type listType = toList.ReturnType;
			CodeInstruction next = i + 1 < codes.Count ? codes[i + 1] : null;
			CodeInstruction call;
			if (next != null && next.opcode == OpCodes.Stfld && next.operand is FieldInfo destField)
			{
				var ldarg = new CodeInstruction(OpCodes.Ldarg_0);
				ldarg.labels.AddRange(codes[i].labels);
				codes[i].labels.Clear();
				codes.Insert(i, ldarg);
				codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldfld, destField));
				i += 2;
				call = new CodeInstruction(OpCodes.Call, fillDest);
			}
			else
			{
				call = new CodeInstruction(OpCodes.Call, fillClient);
				call.labels.AddRange(codes[i].labels);
				codes[i].labels.Clear();
			}

			codes[i] = call;
			codes.Insert(i + 1, new CodeInstruction(OpCodes.Castclass, listType));
			replaced++;
			i++;
		}

		if (replaced == 0)
			OptLog.Warn("[KrokMPOpt2] CoolSync ToList transpiler found no Enumerable.ToList calls");
		return codes;
	}

	internal static NetDataWriter RentWriter(ushort msgid)
	{
		if (_packWriter == null)
			_packWriter = new NetDataWriter(true, 1024);
		else
			_packWriter.Reset();
		_packWriter.Put(msgid);
		MemoryTelemetry.HitPackWriterReuse();
		return _packWriter;
	}

	internal static NetDataWriter RentWriter(in Enum msgid) =>
		RentWriter((ushort)(object)msgid);

	internal static List<CodeInstruction> RewriteCreateWriter(List<CodeInstruction> codes)
	{
		int replaced = 0;
		for (int i = 0; i < codes.Count; i++)
		{
			if (codes[i].opcode != OpCodes.Call && codes[i].opcode != OpCodes.Callvirt)
				continue;
			if (codes[i].operand is not MethodInfo mi)
				continue;
			if (mi.Name != "CreateWriter" || mi.DeclaringType != typeof(Net))
				continue;
			ParameterInfo[] ps = mi.GetParameters();
			if (ps.Length != 1)
				continue;

			MethodInfo rent = FindRentWriter(ps[0].ParameterType);
			if (rent == null)
				continue;

			var call = new CodeInstruction(OpCodes.Call, rent);
			call.labels.AddRange(codes[i].labels);
			codes[i] = call;
			replaced++;
		}

		if (replaced == 0)
			OptLog.Warn("[KrokMPOpt2] CoolSync CreateWriter transpiler found no Net.CreateWriter calls");
		return codes;
	}

	private static MethodInfo FindRentWriter(Type paramType)
	{
		foreach (MethodInfo mi in typeof(CoolSyncAlloc).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
		{
			if (mi.Name != nameof(RentWriter))
				continue;
			ParameterInfo[] ps = mi.GetParameters();
			if (ps.Length == 1 && ps[0].ParameterType == paramType)
				return mi;
		}

		return AccessTools.Method(typeof(CoolSyncAlloc), nameof(RentWriter), new[] { paramType });
	}

	internal static List<CodeInstruction> RewriteKnetidKeyLists(List<CodeInstruction> codes)
	{
		MethodInfo fill = AccessTools.Method(typeof(CoolSyncAlloc), nameof(FillKnetidKeys));
		int replaced = 0;
		for (int i = 0; i < codes.Count; i++)
		{
			if (codes[i].opcode != OpCodes.Newobj || codes[i].operand is not ConstructorInfo ctor)
				continue;
			if (ctor.DeclaringType != typeof(List<knetid>))
				continue;
			ParameterInfo[] ps = ctor.GetParameters();
			if (ps.Length != 1)
				continue;

			var call = new CodeInstruction(OpCodes.Call, fill);
			call.labels.AddRange(codes[i].labels);
			codes[i] = call;
			replaced++;
		}

		if (replaced == 0)
			OptLog.Warn("[KrokMPOpt2] CoolSync key-list transpiler found no List<knetid> copies");
		return codes;
	}

	private static bool IsToList(CodeInstruction ins, out MethodInfo toList)
	{
		toList = null;
		if (ins.opcode != OpCodes.Call && ins.opcode != OpCodes.Callvirt)
			return false;
		if (ins.operand is not MethodInfo mi)
			return false;
		if (mi.Name != "ToList" || mi.DeclaringType != typeof(Enumerable))
			return false;
		toList = mi;
		return true;
	}
}

internal static class SnapshotPool
{
	private const int MaxPool = 512;
	private static readonly Stack<ServerSnapshot> Pool = new Stack<ServerSnapshot>(64);

	internal static int PooledCount => Pool.Count;

	internal static ServerSnapshot Rent()
	{
		if (Pool.Count > 0)
		{
			ServerSnapshot snap = Pool.Pop();
			snap.objects_it_contains.Clear();
			snap.deltaid = 0;
			snap.is_deleted = false;
			MemoryTelemetry.HitSnapRent();
			return snap;
		}

		return new ServerSnapshot();
	}

	internal static void Return(object snap)
	{
		if (snap is ServerSnapshot typed)
			Return(typed);
	}

	internal static void Return(ServerSnapshot snap)
	{
		if (snap == null)
			return;
		snap.objects_it_contains.Clear();
		snap.deltaid = 0;
		snap.is_deleted = false;
		if (Pool.Count < MaxPool)
			Pool.Push(snap);
	}

	internal static void ReclaimDict(IDictionary snaps)
	{
		if (snaps == null || snaps.Count == 0)
			return;
		foreach (DictionaryEntry e in snaps)
			Return(e.Value);
		snaps.Clear();
	}

	internal static void ReclaimAll()
	{
		try
		{
			if (!CoolSyncReflect.Ok && !CoolSyncReflect.TryResolve())
				return;
			if (AccessTools.Field(typeof(CoolSyncManager), "all_systems")?.GetValue(null) is not IDictionary all)
				return;
			foreach (DictionaryEntry e in all)
			{
				if (e.Value == null || CoolSyncReflect.PerPlrStates == null)
					continue;
				if (CoolSyncReflect.PerPlrStates.GetValue(e.Value) is not IDictionary states)
					continue;
				foreach (DictionaryEntry pe in states)
				{
					if (pe.Value == null)
						continue;
					ReclaimDict(CoolSyncReflect.PP_Snapshots.GetValue(pe.Value) as IDictionary);
				}
			}
		}
		catch (Exception ex)
		{
			OptLog.Warn($"[KrokMPOpt2] snapshot reclaim skipped: {ex.Message}");
		}
	}
}

[HarmonyPatch(typeof(CoolSyncSubSystemForObjects), "PackAndSend", typeof(bool))]
internal static class CoolSyncPackAndSendToListPatch
{
	static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
		CoolSyncAlloc.RewriteToListCalls(new List<CodeInstruction>(instructions));
}

[HarmonyPatch(typeof(CoolSyncSubSystemForObjects), "Update")]
internal static class CoolSyncUpdateToListPatch
{
	static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
		CoolSyncAlloc.RewriteToListCalls(new List<CodeInstruction>(instructions));
}

[HarmonyPatch(typeof(CoolSyncSubSystemForObjects), "Server_ClearOldPlayers")]
internal static class CoolSyncClearOldPlayersPatch
{
	static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
		CoolSyncAlloc.RewriteKnetidKeyLists(new List<CodeInstruction>(instructions));
}

[HarmonyPatch(typeof(CoolSyncSubSystemStatic), "Server_ClearOldPlayers")]
internal static class CoolSyncStaticClearOldPlayersPatch
{
	static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
		CoolSyncAlloc.RewriteKnetidKeyLists(new List<CodeInstruction>(instructions));
}

[HarmonyPatch(typeof(CoolSyncManager), "FixedUpdate")]
internal static class CoolSyncManagerFixedUpdatePatch
{
	private static FieldInfo _allSystems;
	private static BaseCoolSyncSubSystem[] _cache = Array.Empty<BaseCoolSyncSubSystem>();
	private static int _cacheCount = -1;

	static bool Prefix(CoolSyncManager __instance)
	{
		if (!CoolSyncAlloc.Enabled)
			return true;

		_allSystems ??= AccessTools.Field(typeof(CoolSyncManager), "all_systems");
		if (_allSystems?.GetValue(null) is not IDictionary all)
			return true;

		if (!KrokoshaScavMultiplayer.IsNetworkActiveAndIsServer() || all.Count == 0)
		{
			_cacheCount = 0;
			return false;
		}

		if (all.Count != _cacheCount)
			Rebuild(all);

		int count = _cache.Length;
		bool sent = false;
		int idx = __instance.cur_system_to_send;
		for (int i = 0; i < count; i++)
		{
			if (idx >= count)
				idx = 0;
			try
			{
				BaseCoolSyncSubSystem sys = _cache[idx];
				if (sys != null)
					sent = sys.Server_Update();
			}
			catch (Exception ex)
			{
				OptLog.Error("CoolSync.FixedUpdate", ex);
			}

			idx++;
			if (sent)
				break;
		}

		__instance.cur_system_to_send = idx;
		return false;
	}

	private static void Rebuild(IDictionary all)
	{
		int n = all.Count;
		if (_cache.Length != n)
			_cache = new BaseCoolSyncSubSystem[n];
		int i = 0;
		foreach (DictionaryEntry e in all)
			_cache[i++] = e.Value as BaseCoolSyncSubSystem;
		_cacheCount = n;
	}
}

[HarmonyPatch(typeof(CoolSyncSubSystemForObjects), "Server_OneStepClearOldSnapshots")]
internal static class CoolSyncOneStepClearPatch
{
	static bool Prefix(object plrstate, ref bool __result)
	{
		if (!CoolSyncAlloc.Enabled)
			return true;
		if (plrstate is not ServerPerPlr state)
			return true;

		if (state.snapshot_queue.Count < 2)
		{
			__result = false;
			return false;
		}

		ushort deltaid = state.snapshot_queue.Peek();
		foreach (var kv in state.objstates)
		{
			if (kv.Value != null && kv.Value.last_known_snapshot_id == deltaid)
			{
				__result = false;
				return false;
			}
		}

		deltaid = state.snapshot_queue.Dequeue();
		if (state.snapshots.TryGetValue(deltaid, out ServerSnapshot snap))
		{
			state.snapshots.Remove(deltaid);
			SnapshotPool.Return(snap);
		}
		else
			state.snapshots.Remove(deltaid);

		__result = true;
		return false;
	}
}

[HarmonyPatch(typeof(CoolSyncSubSystemForObjects), "SaveSnapshot")]
internal static class CoolSyncSaveSnapshotPoolPatch
{
	static bool Prefix(CoolSyncSubSystemForObjects __instance, object perplr, object obj)
	{
		if (!CoolSyncAlloc.Enabled)
			return true;
		if (perplr is not ServerPerPlr state || obj == null)
			return true;

		ushort roll = state.cur_delta_roll;
		if (!state.snapshots.TryGetValue(roll, out ServerSnapshot value))
		{
			value = SnapshotPool.Rent();
			value.deltaid = roll;
			value.is_deleted = CoolSyncReflect.SO_RealObj?.GetValue(obj) == null;
			state.snapshots[roll] = value;
			state.snapshot_queue.Enqueue(roll);

			int cap = 2000;
			if (CoolSyncReflect.MaxSnapshotQueue != null)
				cap = (int)CoolSyncReflect.MaxSnapshotQueue.GetValue(__instance);

			if (state.snapshot_queue.Count > cap)
			{
				float frac = Plugin.QueueOverflowFraction?.Value ?? 0.1f;
				if (frac < 0.01f)
					frac = 0.01f;
				if (frac > 1f)
					frac = 1f;
				int drop = Mathf.CeilToInt(cap * frac);
				for (int i = 0; i < drop && state.snapshot_queue.Count > 0; i++)
				{
					ushort key = state.snapshot_queue.Dequeue();
					if (state.snapshots.TryGetValue(key, out ServerSnapshot old))
					{
						state.snapshots.Remove(key);
						SnapshotPool.Return(old);
					}
					else
						state.snapshots.Remove(key);
				}
			}
		}

		if (CoolSyncReflect.SO_NetId?.GetValue(obj) is knetid netId
		    && !value.objects_it_contains.ContainsKey(netId))
		{
			value.objects_it_contains[netId] = CoolSyncReflect.SO_CurPacket?.GetValue(obj) as IDeltaPacketBase;
		}

		return false;
	}
}

[HarmonyPatch(typeof(BaseCoolSyncSubSystem), "BoolListToByteBitset")]
internal static class CoolSyncBitsetPoolPatch
{
	static bool Prefix(List<bool> bits, ref byte[] __result)
	{
		if (!CoolSyncAlloc.Enabled || bits == null)
			return true;
		__result = CoolSyncAlloc.FillBitset(bits);
		return false;
	}
}

[HarmonyPatch(typeof(CoolSyncManager), "OnTransportEnd")]
internal static class CoolSyncSnapshotReclaimPatch
{
	static void Prefix()
	{
		if (CoolSyncAlloc.Enabled)
			SnapshotPool.ReclaimAll();
	}
}

[HarmonyPatch(typeof(CoolSyncSubSystemForObjects), "PackAndSend", typeof(bool))]
internal static class CoolSyncPackAndSendWriterPatch
{
	static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
		CoolSyncAlloc.RewriteCreateWriter(new List<CodeInstruction>(instructions));
}

[HarmonyPatch(typeof(CoolSyncSubSystemStatic), "PackAndSend")]
internal static class CoolSyncStaticPackAndSendWriterPatch
{
	static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
		CoolSyncAlloc.RewriteCreateWriter(new List<CodeInstruction>(instructions));
}

[HarmonyPatch]
internal static class PlrSyncPackPacketPatch
{
	static MethodBase TargetMethod()
	{
		Type t = AccessTools.TypeByName("KrokoshaCasualtiesMP.PlrSync");
		return AccessTools.Method(t, "PackPacket");
	}

	static bool Prefix(object obj)
	{
		if (obj == null)
			return true;
		if (!CoolSyncReflect.Ok && !CoolSyncReflect.TryResolve())
			return true;
		if (CoolSyncReflect.SO_CurPacket == null || CoolSyncReflect.SO_NetId == null || CoolSyncReflect.SO_RealObj == null)
			return true;

		var packet = new PlayerSyncPacket();
		packet.SetDefault();
		object real = CoolSyncReflect.SO_RealObj.GetValue(obj);
		if (real is NetPlayer netPlayer)
		{
			packet.AutoSerialize((knetid)CoolSyncReflect.SO_NetId.GetValue(obj), netPlayer);
			packet.camerapos = netPlayer.camerapos;
			packet.unchipped = netPlayer.IsUnchipped();
		}

		CoolSyncReflect.SO_CurPacket.SetValue(obj, packet);
		MemoryTelemetry.HitPlrPackSkipClone();
		return false;
	}
}
