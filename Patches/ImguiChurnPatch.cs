using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace KrokMPOptimization2;

[HarmonyPatch(typeof(UIBullshit), "_GUI_DoTexturesForMPUserInterface")]
// skips redundant IMGUI skin, overlay, and in-world UI work when nothing changed.
internal static class ImguiSkinSkipPatch
{
	private static FieldInfo _recalc;
	private static float _lastScale = float.NaN;
	private static int _lastW = -1;
	private static int _lastH = -1;
	internal static bool SkipThisSetSkin;

	static void Prefix()
	{
		SkipThisSetSkin = false;
		if (Plugin.SkipIdleSkinRebuild == null || !Plugin.SkipIdleSkinRebuild.Value)
			return;

		_recalc ??= AccessTools.Field(typeof(UIBullshit), "_recalculate_skin");
		bool recalc = _recalc != null && _recalc.GetValue(null) is true;
		float scale = UIBullshit.uiScale;
		int w = Screen.width;
		int h = Screen.height;
		bool scaleChanged = float.IsNaN(_lastScale)
		                    || Mathf.Abs(scale - _lastScale) > 0.0001f
		                    || w != _lastW
		                    || h != _lastH;

		if (recalc || scaleChanged)
		{
			_lastScale = scale;
			_lastW = w;
			_lastH = h;
			return;
		}

		SkipThisSetSkin = true;
	}
}

[HarmonyPatch(typeof(UIBullshit), "_GUI_MPUISetSkinValues")]
internal static class ImguiSkinSkipApplyPatch
{
	static bool Prefix()
	{
		if (!ImguiSkinSkipPatch.SkipThisSetSkin)
			return true;
		MemoryTelemetry.HitSkinSkip();
		return false;
	}
}

[HarmonyPatch(typeof(UIBullshit), "_GUI_DoTheFullGUI")]
internal static class ImguiInGameUiResetPatch
{
	internal static int InGameUiCalls;

	static void Prefix()
	{
		InGameUiCalls = 0;
	}
}

[HarmonyPatch(typeof(UIInGame), "_GUI_DoInGameUI")]
internal static class ImguiInGameUiDedupPatch
{
	static bool Prefix()
	{
		if (Plugin.DeduplicateInGameUi == null || !Plugin.DeduplicateInGameUi.Value)
			return true;
		if (ImguiInGameUiResetPatch.InGameUiCalls <= 0)
		{
			ImguiInGameUiResetPatch.InGameUiCalls++;
			return true;
		}

		MemoryTelemetry.HitUiDedup();
		return false;
	}
}

[HarmonyPatch(typeof(KrokoshaScavMultiplayer), "_GUI_RenderMainGUI")]
internal static class ImguiCoopOverlaySkipPatch
{
	static bool Prefix()
	{
		if (Plugin.ShowCoopOverlay != null && Plugin.ShowCoopOverlay.Value)
			return true;
		if (Plugin.VerboseLogging != null && Plugin.VerboseLogging.Value)
			return true;

		MemoryTelemetry.HitOverlaySkip();
		return false;
	}
}

internal static class CatPatchStyleCachePatch
{
	private static GUIStyle _cached;
	private static float _scale = float.NaN;
	private static int _skinId;
	private static int _width;

	internal static bool Prefix(ref GUIStyle __result)
	{
		float scale = UIBullshit.uiScale;
		int skinId = GUI.skin != null ? GUI.skin.GetInstanceID() : 0;
		if (_cached != null
		    && !float.IsNaN(_scale)
		    && Mathf.Abs(scale - _scale) < 0.0001f
		    && skinId == _skinId
		    && Screen.width == _width)
		{
			__result = _cached;
			MemoryTelemetry.HitCatStyle();
			return false;
		}

		return true;
	}

	internal static void Postfix(GUIStyle __result)
	{
		if (__result == null)
			return;
		_cached = __result;
		_scale = UIBullshit.uiScale;
		_skinId = GUI.skin != null ? GUI.skin.GetInstanceID() : 0;
		_width = Screen.width;
	}
}

internal static class CatPatchMenuSkipPatch
{
	internal static bool Prefix()
	{
		if (Plugin.SkipHiddenCatPatchMenu != null && !Plugin.SkipHiddenCatPatchMenu.Value)
			return true;
		if (ShouldShowMenuButton())
			return true;

		MemoryTelemetry.HitCatMenuSkip();
		return false;
	}

	internal static bool ShouldShowMenuButton()
	{
		try
		{
			if (IsBaseRunSettingsOpen())
				return false;
			if (UIBullshit.IsAnyMenuOpen())
				return true;
			if (UIMainMenu.mainmenu_open || UIMainMenu.IsOpen())
				return true;
			return PlayerCamera.main == null;
		}
		catch
		{
			return true;
		}
	}

	private static bool IsBaseRunSettingsOpen()
	{
		try
		{
			return PreRunScript.instance != null
			       && PreRunScript.instance.runSettingsScreen != null
			       && PreRunScript.instance.runSettingsScreen.activeInHierarchy;
		}
		catch
		{
			return false;
		}
	}
}
