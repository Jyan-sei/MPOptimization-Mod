using System;
using HarmonyLib;

namespace KrokMPOptimization2;

// registers the memory, pooling, and cleanup patches.
internal static class MemoryBootstrap
{
	internal static bool CatPatchHooked { get; private set; }

	private static Harmony _harmony;
	private static int _catPatchTries;

	internal static void Register(Harmony harmony)
	{
		_harmony = harmony;
		TryPatch(harmony, typeof(ImguiSkinSkipPatch));
		TryPatch(harmony, typeof(ImguiSkinSkipApplyPatch));
		TryPatch(harmony, typeof(ImguiInGameUiResetPatch));
		TryPatch(harmony, typeof(ImguiInGameUiDedupPatch));
		TryPatch(harmony, typeof(ImguiCoopOverlaySkipPatch));
		TryPatch(harmony, typeof(CompressPoolGzipPatch));
		TryPatch(harmony, typeof(CompressPoolDeflatePatch));
		TryPatch(harmony, typeof(UnconsciousTextPatch));
		TryPatch(harmony, typeof(ObjStatePruneLayerFlushPatch));
		TryPatch(harmony, typeof(MemoryTransportEndPatch));
		TryPatch(harmony, typeof(SteamAvatarOnDestroyPatch));
		TryPatch(harmony, typeof(UiTextureDestroyPatch));
		TryPatch(harmony, typeof(LimbMaterialAwakePatch));
		TryPatch(harmony, typeof(NametagFancyMaterialPatch));
		TryRegisterCatPatch(harmony);

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
			$"[KrokMPOpt2] memory patches armed v{PluginInfo.Version} catPatchHooked={(CatPatchHooked ? 1 : 0)}");
	}

	internal static void TickLateCatPatch()
	{
		if (CatPatchHooked || _harmony == null || _catPatchTries >= 8)
			return;
		_catPatchTries++;
		TryRegisterCatPatch(_harmony);
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

	private static void TryRegisterCatPatch(Harmony harmony)
	{
		try
		{
			Type menu = AccessTools.TypeByName("CatPatchspace.CatPatchHostSettingsMenu");
			if (menu == null)
			{
				CatPatchHooked = false;
				if (_catPatchTries >= 8)
					OptLog.Info("[KrokMPOpt2] memory catPatchHooked=0 (type missing)");
				return;
			}

			var create = AccessTools.Method(menu, "CreateKrokStyleButton");
			if (create == null)
			{
				CatPatchHooked = false;
				OptLog.Info("[KrokMPOpt2] memory catPatchHooked=0 (CreateKrokStyleButton missing)");
				return;
			}

			harmony.Patch(create,
				prefix: new HarmonyMethod(typeof(CatPatchStyleCachePatch), nameof(CatPatchStyleCachePatch.Prefix)),
				postfix: new HarmonyMethod(typeof(CatPatchStyleCachePatch), nameof(CatPatchStyleCachePatch.Postfix)));

			var onGui = AccessTools.Method(menu, "OnGUI");
			if (onGui != null)
			{
				harmony.Patch(onGui,
					prefix: new HarmonyMethod(typeof(CatPatchMenuSkipPatch), nameof(CatPatchMenuSkipPatch.Prefix)));
			}
			else
				OptLog.Info("[KrokMPOpt2] memory catPatch OnGUI missing");

			var drawOverlay = AccessTools.Method(menu, "DrawOverlayFromKrokMenu");
			if (drawOverlay != null)
			{
				harmony.Patch(drawOverlay,
					prefix: new HarmonyMethod(typeof(CatPatchMenuSkipPatch), nameof(CatPatchMenuSkipPatch.Prefix)));
			}
			else
				OptLog.Info("[KrokMPOpt2] memory catPatch DrawOverlayFromKrokMenu missing");

			CatPatchHooked = true;
			OptLog.Info("[KrokMPOpt2] memory catPatchHooked=1");
		}
		catch (Exception ex)
		{
			CatPatchHooked = false;
			OptLog.Warn($"[KrokMPOpt2] memory catPatchHooked=0 ({ex.Message})");
		}
	}
}
