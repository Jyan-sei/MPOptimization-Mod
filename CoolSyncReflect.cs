using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;

namespace KrokMPOptimization2;

// resolves the private CoolSync types and members used by the queue patches.
internal static class CoolSyncReflect
{
	internal static bool Ok { get; private set; }

	internal static Type CoolSyncType;
	internal static Type StaticType;
	internal static Type ObjectSyncType;
	internal static Type PerPlrType;
	internal static Type ServerObjectType;
	internal static Type StaticPerPlrType;
	internal static Type ObjStateType;

	internal static FieldInfo BasePacket;
	internal static FieldInfo ServerObjects;
	internal static FieldInfo PerPlrStates;
	internal static FieldInfo StaticPerPlrStates;
	internal static FieldInfo MaxSnapshotQueue;
	internal static FieldInfo StaticMaxSnapshotQueue;
	internal static FieldInfo StaticSnapshotQueue;
	internal static FieldInfo StaticSnapshots;
	internal static FieldInfo StaticDeltaCounter;
	internal static FieldInfo ServerHasQueuedForceSync;

	internal static FieldInfo SO_NetId;
	internal static FieldInfo SO_CurPacket;
	internal static FieldInfo SO_RealObj;
	internal static FieldInfo SO_CurFramePacked;

	internal static FieldInfo PP_Snapshots;
	internal static FieldInfo PP_SnapshotQueue;
	internal static FieldInfo PP_ObjStates;
	internal static FieldInfo PP_CurDelta;
	internal static FieldInfo PP_ForceSync;
	internal static FieldInfo PP_ClearQueued;
	internal static FieldInfo PP_PlrId;

	internal static FieldInfo SP_LastKnownId;

	internal static FieldInfo OS_LastKnown;
	internal static FieldInfo OS_LastKnownId;

	internal static MethodInfo GetPerPlrState;
	internal static MethodInfo GetObjState;
	internal static MethodInfo OneStepClear;
	internal static MethodInfo SaveSnapshot;
	internal static MethodInfo ServerReceiveAck;
	internal static MethodInfo PackPacket;
	internal static MethodInfo Deallocate;
	internal static MethodInfo QueueForceSync;
	internal static MethodInfo QueueForceSyncForAll;
	internal static MethodInfo InternalQueueForceSync;
	internal static MethodInfo ServerDeleteObject;

	internal static FieldInfo ObjectInst;
	internal static FieldInfo CharInst;

	internal static PropertyInfo SyncSystemId;

	internal static bool TryResolve()
	{
		try
		{
			CoolSyncType = AccessTools.TypeByName("KrokoshaCasualtiesMP.CoolSyncSubSystemForObjects");
			StaticType = AccessTools.TypeByName("KrokoshaCasualtiesMP.CoolSyncSubSystemStatic");
			ObjectSyncType = AccessTools.TypeByName("KrokoshaCasualtiesMP.NewCoolerObjectPacketWriteReadSystem");
			if (CoolSyncType == null || StaticType == null || ObjectSyncType == null)
				return Ok = false;

			ServerObjectType = AccessTools.Inner(CoolSyncType, "Server_Object");
			PerPlrType = AccessTools.Inner(CoolSyncType, "Server_PerPlrState");
			StaticPerPlrType = AccessTools.Inner(StaticType, "Server_PerPlrState");
			ObjStateType = AccessTools.Inner(PerPlrType, "Server_PerPlrObjectState");
			if (ServerObjectType == null || PerPlrType == null || ObjStateType == null)
				return Ok = false;

			BasePacket = AccessTools.Field(CoolSyncType, "base_packet");
			ServerObjects = AccessTools.Field(CoolSyncType, "server_objects");
			PerPlrStates = AccessTools.Field(CoolSyncType, "server_perplrstates");
			StaticPerPlrStates = AccessTools.Field(StaticType, "server_perplrstates");
			MaxSnapshotQueue = AccessTools.Field(CoolSyncType, "max_snapshot_queue");
			ServerHasQueuedForceSync = AccessTools.Field(CoolSyncType, "server_has_queued_forcesync");

			StaticMaxSnapshotQueue = AccessTools.Field(StaticType, "max_snapshot_queue");
			StaticSnapshotQueue = AccessTools.Field(StaticType, "server_snapshot_queue");
			StaticSnapshots = AccessTools.Field(StaticType, "server_snapshots");
			StaticDeltaCounter = AccessTools.Field(StaticType, "server_delta_roll_counter");

			SO_NetId = AccessTools.Field(ServerObjectType, "netId");
			SO_CurPacket = AccessTools.Field(ServerObjectType, "cur_packet");
			SO_RealObj = AccessTools.Field(ServerObjectType, "real_obj");
			SO_CurFramePacked = AccessTools.Field(ServerObjectType, "cur_frame_packed");

			PP_Snapshots = AccessTools.Field(PerPlrType, "snapshots");
			PP_SnapshotQueue = AccessTools.Field(PerPlrType, "snapshot_queue");
			PP_ObjStates = AccessTools.Field(PerPlrType, "objstates");
			PP_CurDelta = AccessTools.Field(PerPlrType, "cur_delta_roll");
			PP_ForceSync = AccessTools.Field(PerPlrType, "forcesync_queue");
			PP_ClearQueued = AccessTools.Field(PerPlrType, "clear_queued");
			PP_PlrId = AccessTools.Field(PerPlrType, "plrId");

			SP_LastKnownId = StaticPerPlrType != null
				? AccessTools.Field(StaticPerPlrType, "last_known_snapshot_id")
				: null;

			OS_LastKnown = AccessTools.Field(ObjStateType, "last_known_snapshot");
			OS_LastKnownId = AccessTools.Field(ObjStateType, "last_known_snapshot_id");

			GetPerPlrState = AccessTools.Method(CoolSyncType, "GetPerPlrState");
			GetObjState = AccessTools.Method(PerPlrType, "GetObjState");
			OneStepClear = AccessTools.Method(CoolSyncType, "Server_OneStepClearOldSnapshots");
			SaveSnapshot = AccessTools.Method(CoolSyncType, "SaveSnapshot");
			ServerReceiveAck = AccessTools.Method(CoolSyncType, "Server_ReceiveAck");
			PackPacket = AccessTools.Method(CoolSyncType, "PackPacket", new[] { ServerObjectType });
			Deallocate = AccessTools.Method(CoolSyncType, "Server_Internal_DeallocateObject");
			QueueForceSync = AccessTools.Method(CoolSyncType, "Server_QueueForceSync");
			QueueForceSyncForAll = AccessTools.Method(CoolSyncType, "Server_QueueForceSyncForAll");
			InternalQueueForceSync = AccessTools.Method(CoolSyncType, "Server_Internal_QueueForceSync");
			ServerDeleteObject = AccessTools.Method(ObjectSyncType, "Server_DeleteObject", new[] { typeof(knetid) })
				?? AccessTools.Method(CoolSyncType, "Server_DeleteObject", new[] { typeof(knetid) });

			ObjectInst = AccessTools.Field(ObjectSyncType, "inst");
			CharInst = AccessTools.Field(AccessTools.TypeByName("KrokoshaCasualtiesMP.CharSync"), "inst");

			SyncSystemId = AccessTools.Property(typeof(BaseCoolSyncSubSystem), "syncsystemid");

			Ok = BasePacket != null && ServerObjects != null && PerPlrStates != null
			     && MaxSnapshotQueue != null && PP_SnapshotQueue != null && PP_Snapshots != null
			     && PP_ObjStates != null && PP_CurDelta != null && OS_LastKnownId != null
			     && GetPerPlrState != null && OneStepClear != null && SaveSnapshot != null
			     && ServerReceiveAck != null && PackPacket != null && Deallocate != null
			     && QueueForceSync != null && InternalQueueForceSync != null
			     && StaticMaxSnapshotQueue != null && StaticSnapshotQueue != null;
			return Ok;
		}
		catch (Exception ex)
		{
			OptLog.Error("CoolSyncReflect", ex);
			return Ok = false;
		}
	}

	internal static int QueueCount(object perPlr) =>
		PP_SnapshotQueue.GetValue(perPlr) is ICollection c ? c.Count : 0;

	internal static bool TryPeekQueueHead(object perPlr, out ushort head)
	{
		head = 0;
		if (PP_SnapshotQueue.GetValue(perPlr) is not IEnumerable queue)
			return false;
		foreach (object idObj in queue)
		{
			if (idObj is ushort deltaId)
			{
				head = deltaId;
				return true;
			}
			break;
		}
		return false;
	}

	internal static bool TryPeekStaticQueueHead(object subsystem, out ushort head)
	{
		head = 0;
		if (StaticSnapshotQueue?.GetValue(subsystem) is not IEnumerable queue)
			return false;
		foreach (object idObj in queue)
		{
			if (idObj is ushort deltaId)
			{
				head = deltaId;
				return true;
			}
			break;
		}
		return false;
	}

	internal static int GetQueueHeadPinners(object perPlr)
	{
		if (!TryPeekQueueHead(perPlr, out ushort head))
			return 0;
		if (PP_ObjStates.GetValue(perPlr) is not IDictionary objstates)
			return 0;

		int count = 0;
		foreach (DictionaryEntry e in objstates)
		{
			if (e.Value == null)
				continue;
			ushort lastKnown = (ushort)OS_LastKnownId.GetValue(e.Value);
			if (lastKnown == head)
				count++;
		}
		return count;
	}

	internal static int GetStaticQueueHeadPinners(object subsystem)
	{
		if (!TryPeekStaticQueueHead(subsystem, out ushort head))
			return 0;
		if (StaticPerPlrStates.GetValue(subsystem) is not IDictionary states || SP_LastKnownId == null)
			return 0;

		int count = 0;
		foreach (DictionaryEntry e in states)
		{
			if (e.Value == null)
				continue;
			ushort lastKnown = (ushort)SP_LastKnownId.GetValue(e.Value);
			if (lastKnown == head)
				count++;
		}
		return count;
	}

	internal static string SubsystemLabel(object subsystem)
	{
		if (subsystem == null)
			return "?";
		string t = subsystem.GetType().Name;
		if (t.Contains("NewCooler"))
			return "ObjectSync";
		if (t == "CharSync")
			return "CharSync";
		return t;
	}

	internal static object GetObjectSyncInst() => ObjectInst?.GetValue(null);

	/// <summary>
	/// stock CoolSync delete: clears server real_obj and sends a zero-length packet.
	/// vanilla clients run Client_DeleteObject and destroy the game object. the host object stays alive.
	/// </summary>
	internal static bool TryServerDeleteObject(object syncInst, knetid netId)
	{
		if (syncInst == null)
			return false;
		MethodInfo delete = ServerDeleteObject
			?? AccessTools.Method(syncInst.GetType(), "Server_DeleteObject", new[] { typeof(knetid) });
		if (delete == null)
			return false;
		delete.Invoke(syncInst, new object[] { netId });
		return true;
	}
}
