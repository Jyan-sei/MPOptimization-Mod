using System;
using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;

namespace KrokMPOptimization2;

// records wall-time averages for stock KrokMP and plugin hot paths.
[HarmonyPatch]
internal static class FrameTimingPatches
{
	internal static void Register(Harmony harmony)
	{
		Patch(harmony, typeof(FrameTiming_CoolSyncManagerUpdate));
		Patch(harmony, typeof(FrameTiming_ObjectSyncUpdate));
		Patch(harmony, typeof(FrameTiming_CharSyncUpdate));
		Patch(harmony, typeof(FrameTiming_FastSyncUpdate));
		Patch(harmony, typeof(FrameTiming_SaveSnapshot));
		Patch(harmony, typeof(FrameTiming_OneStepClear));
		Patch(harmony, typeof(FrameTiming_PackObjectForPlr));
		Patch(harmony, typeof(FrameTiming_ReceiveAck));
		Patch(harmony, typeof(FrameTiming_PackSendUpdate));
		Patch(harmony, typeof(FrameTiming_NetObjectRegistryUpdate));
	}

	private static void Patch(Harmony harmony, Type patchType)
	{
		try
		{
			harmony.PatchAll(patchType);
		}
		catch (Exception ex)
		{
			OptLog.Warn($"[KrokMPOpt2] timing patch skipped {patchType.Name}: {ex.Message}");
		}
	}
}

[HarmonyPatch]
internal static class FrameTiming_CoolSyncManagerUpdate
{
	static MethodBase TargetMethod() =>
		AccessTools.Method(typeof(CoolSyncManager), "Update");

	static void Prefix(ref double __state) => __state = FrameTiming.Begin();
	static void Finalizer(double __state) => FrameTiming.End("coolMgr", __state);
}

[HarmonyPatch]
internal static class FrameTiming_ObjectSyncUpdate
{
	static MethodBase TargetMethod() =>
		AccessTools.Method(CoolSyncReflect.CoolSyncType, "Update");

	static void Prefix(ref double __state) => __state = FrameTiming.Begin();
	static void Finalizer(double __state) => FrameTiming.End("objSyncUpd", __state);
}

[HarmonyPatch]
internal static class FrameTiming_CharSyncUpdate
{
	static MethodBase TargetMethod() =>
		AccessTools.Method(AccessTools.TypeByName("KrokoshaCasualtiesMP.CharSync"), "Server_Update");

	static void Prefix(ref double __state) => __state = FrameTiming.Begin();
	static void Finalizer(double __state) => FrameTiming.End("charSyncUpd", __state);
}

[HarmonyPatch]
internal static class FrameTiming_FastSyncUpdate
{
	static MethodBase TargetMethod() =>
		AccessTools.Method(CoolSyncReflect.CoolSyncType, "Server_FastSyncUpdate");

	static void Prefix(ref double __state) => __state = FrameTiming.Begin();
	static void Finalizer(double __state) => FrameTiming.End("fastSyncTick", __state);
}

[HarmonyPatch]
internal static class FrameTiming_SaveSnapshot
{
	static MethodBase TargetMethod() => CoolSyncReflect.SaveSnapshot;

	static void Prefix(ref double __state) => __state = FrameTiming.Begin();
	static void Finalizer(double __state) => FrameTiming.End("saveSnap", __state);
}

[HarmonyPatch]
internal static class FrameTiming_OneStepClear
{
	static MethodBase TargetMethod() => CoolSyncReflect.OneStepClear;

	static void Prefix(ref double __state) => __state = FrameTiming.Begin();
	static void Finalizer(double __state) => FrameTiming.End("drain", __state);
}

[HarmonyPatch]
internal static class FrameTiming_PackObjectForPlr
{
	static MethodBase TargetMethod() =>
		AccessTools.Method(CoolSyncReflect.CoolSyncType, "PackObjectForPlr");

	static void Prefix(ref double __state) => __state = FrameTiming.Begin();
	static void Finalizer(double __state) => FrameTiming.End("packObj", __state);
}

[HarmonyPatch]
internal static class FrameTiming_ReceiveAck
{
	static MethodBase TargetMethod() => CoolSyncReflect.ServerReceiveAck;

	static void Prefix(ref double __state) => __state = FrameTiming.Begin();
	static void Finalizer(double __state) => FrameTiming.End("recvAck", __state);
}

[HarmonyPatch]
internal static class FrameTiming_PackSendUpdate
{
	private static PropertyInfo _syncSystemId;

	static MethodBase TargetMethod() =>
		AccessTools.Method(typeof(CoolSyncSubSystemForObjects), "Server_Update");

	static void Prefix(ref double __state) => __state = FrameTiming.Begin();

	static void Finalizer(object __instance, double __state)
	{
		try
		{
			_syncSystemId ??= AccessTools.Property(typeof(BaseCoolSyncSubSystem), "syncsystemid");
			byte id = _syncSystemId?.GetValue(__instance) is byte b ? b : (byte)0;
			FrameTiming.End($"packSend{id}", __state);
		}
		catch
		{
			FrameTiming.End("packSendX", __state);
		}
	}
}

[HarmonyPatch]
internal static class FrameTiming_NetObjectRegistryUpdate
{
	static MethodBase TargetMethod() =>
		AccessTools.Method(typeof(NetObjectRegistry), "Update");

	static void Prefix(ref double __state) => __state = FrameTiming.Begin();
	static void Finalizer(double __state) => FrameTiming.End("netReg", __state);
}
