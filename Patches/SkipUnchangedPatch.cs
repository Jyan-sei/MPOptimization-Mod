using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using LiteNetLib.Utils;
using UnityEngine;

namespace KrokMPOptimization2;

[HarmonyPatch]
// avoids repacking object data for a player who already received the same state.
internal static class SkipUnchangedPatch
{
	private static FieldInfo _sentTo;
	private static FieldInfo _requestedInfo;
	private static FieldInfo _avoidDuplicates;
	private static PropertyInfo _syncSystemId;
	private static bool _loggedFirst;

	static MethodBase TargetMethod() =>
		AccessTools.Method(CoolSyncReflect.CoolSyncType, "PackObjectForPlr");

	static bool Prefix(object __instance, NetDataWriter writer, object plrstate, object obj, ref bool __result)
	{
		if (Plugin.Enabled == null || !Plugin.Enabled.Value || Plugin.SkipUnchangedObjects?.Value != true)
			return true;

		double t0 = FrameTiming.Begin();
		try
		{
			ResolveOnce();

			byte systemId = (byte)_syncSystemId.GetValue(__instance);
			if (systemId != 1)
				return true;

			var netId = CoolSyncReflect.SO_NetId.GetValue(obj);
			if (_avoidDuplicates.GetValue(__instance) is HashSet<knetid> avoid
			    && netId is knetid kid && avoid.Contains(kid))
				return true;

			if (HasWaterContainer(obj))
				return true;

			if (!(bool)CoolSyncReflect.SO_CurFramePacked.GetValue(obj))
			{
				CoolSyncReflect.PackPacket.Invoke(__instance, new[] { obj });
				CoolSyncReflect.SO_CurFramePacked.SetValue(obj, true);
			}

			if (CoolSyncReflect.SO_RealObj.GetValue(obj) == null)
				return true;

			if (!CoolSyncQueueProbe.TryGetPlrId(plrstate, out var plrId))
				return true;

			if (_sentTo.GetValue(obj) is not HashSet<knetid> sent || !sent.Contains(plrId))
				return true;

			if (_requestedInfo.GetValue(obj) is HashSet<knetid> req && req.Contains(plrId))
				return true;

			object objState = CoolSyncReflect.GetObjState.Invoke(plrstate, new[] { netId });
			if (objState == null)
				return true;

			var lastKnown = CoolSyncReflect.OS_LastKnown.GetValue(objState) as IDeltaPacketBase;
			var current = CoolSyncReflect.SO_CurPacket.GetValue(obj) as IDeltaPacketBase;
			if (lastKnown == null || current == null)
				return true;

			if (current is ItemOrBuildingCoolDeltaCompressablePacket a
			    && lastKnown is ItemOrBuildingCoolDeltaCompressablePacket b
			    && a.Equals(b))
			{
				__result = false;
				if (!_loggedFirst && Plugin.VerboseLogging?.Value == true)
				{
					_loggedFirst = true;
					OptLog.Info($"[KrokMPOpt2] first skipUnchanged netId={netId}");
				}
				return false;
			}
		}
		catch (Exception ex)
		{
			OptLog.Error("SkipUnchanged", ex);
		}
		finally
		{
			FrameTiming.End("skipPack", t0);
		}

		return true;
	}

	private static void ResolveOnce()
	{
		if (_sentTo != null)
			return;

		var soType = CoolSyncReflect.ServerObjectType;
		_sentTo = AccessTools.Field(soType, "players_its_been_sent_to");
		_requestedInfo = AccessTools.Field(soType, "players_requested_info");
		_avoidDuplicates = AccessTools.Field(CoolSyncReflect.CoolSyncType, "_packer_avoidduplicates");
		_syncSystemId = AccessTools.Property(typeof(BaseCoolSyncSubSystem), "syncsystemid");
	}

	private static bool HasWaterContainer(object serverObj)
	{
		try
		{
			var real = CoolSyncReflect.SO_RealObj.GetValue(serverObj);
			if (real is not SyncInfo si || (UnityEngine.Object)(object)si.go == (UnityEngine.Object)null)
				return false;
			return si.go.GetComponent("WaterContainerItem") != null;
		}
		catch
		{
			return true;
		}
	}
}
