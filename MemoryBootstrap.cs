using System;
using HarmonyLib;

namespace KrokMPOptimization2;

// registers the memory, pooling, and cleanup patches.
internal static class MemoryBootstrap
{
	private static Harmony _harmony;

	internal static void Register(Harmony harmony)
	{
		_harmony = harmony;

		bool uiOptEnabled = Plugin.UiOptimizationExperimental != null && Plugin.UiOptimizationExperimental.Value;
		if (uiOptEnabled)
		{
			TryPatch(harmony, typeof(ImguiSkinSkipPatch));
			TryPatch(harmony, typeof(ImguiSkinSkipApplyPatch));
			TryPatch(harmony, typeof(ImguiInGameUiResetPatch));
			TryPatch(harmony, typeof(ImguiInGameUiDedupPatch));
			TryPatch(harmony, typeof(UiTextureDestroyPatch));
		}

		TryPatch(harmony, typeof(CompressPoolGzipPatch));
		TryPatch(harmony, typeof(CompressPoolDeflatePatch));
		TryPatch(harmony, typeof(UnconsciousTextPatch));
		TryPatch(harmony, typeof(ObjStatePruneLayerFlushPatch));
		TryPatch(harmony, typeof(MemoryTransportEndPatch));
		TryPatch(harmony, typeof(SteamAvatarOnDestroyPatch));
		TryPatch(harmony, typeof(LimbMaterialAwakePatch));
		TryPatch(harmony, typeof(NametagFancyMaterialPatch));

		if (!CoolSyncReflect.Ok)
			CoolSyncReflect.TryResolve();

		if (Plugin.CoolSyncAllocPooling == null || Plugin.CoolSyncAllocPooling.Value)
		{
			TryPatch(harmony, typeof(CoolSyncPackAndSendToListPatch));
			TryPatch(harmony, typeof(CoolSyncUpdateToListPatch));
			TryPatch(harmony, typeof(CoolSyncClearOldPlayersPatch));
			TryPatch(harmony, typeof(CoolSyncStaticClearOldPlayersPatch));
			TryPatch(harmony, typeof(CoolSyncManagerFixedUpdatePatch));
			TryPatch(harmony, typeof(CoolSyncOneStepClearPatch));
			TryPatch(harmony, typeof(CoolSyncSaveSnapshotPoolPatch));
			TryPatch(harmony, typeof(CoolSyncBitsetPoolPatch));
			TryPatch(harmony, typeof(CoolSyncSnapshotReclaimPatch));
		}

		if (Plugin.CoolSyncWriterPool == null || Plugin.CoolSyncWriterPool.Value)
		{
			TryPatch(harmony, typeof(CoolSyncPackAndSendWriterPatch));
			TryPatch(harmony, typeof(CoolSyncStaticPackAndSendWriterPatch));
		}

		TryPatch(harmony, typeof(PlrSyncPackPacketPatch));

		SteamAvatarPrune.Attach();

		OptLog.Info(
			$"[KrokMPOpt2] memory patches armed v{PluginInfo.Version}");
	}

	private static void TryPatch(Harmony harmony, Type patchType)
	{
		try
		{
			harmony.PatchAll(patchType);
		}
		catch (Exception ex)
		{
			OptLog.Warn($"[KrokMPOpt2] memory patch skipped {patchType.Name}: {ex.Message}");
		}
	}
}
