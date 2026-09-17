using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace KrokMPOptimization2;

[HarmonyPatch(typeof(UIBullshit), "_GUI_DoTexturesForMPUserInterface")]
internal static class ImguiSkinSkipPatch
{
	private static FieldInfo _recalc;
	private static float _lastScale = float.NaN;
	private static int _lastW = -1;
	private static int _lastH = -1;
	private static bool _stamped;
	internal static bool SkipThisSetSkin;
	internal static int StampCount;
	internal static int SkipCount;
	internal static int ReasonOpen;
	internal static int ReasonScale;
	internal static int ReasonFirst;

	static void Prefix()
	{
		SkipThisSetSkin = false;
		if (Plugin.UiOptimizationExperimental == null || !Plugin.UiOptimizationExperimental.Value ||
		    Plugin.SkipIdleSkinRebuild == null || !Plugin.SkipIdleSkinRebuild.Value)
			return;

		_recalc ??= AccessTools.Field(typeof(UIBullshit), "_recalculate_skin");
		bool recalc = _recalc != null && _recalc.GetValue(null) is true;
		float scale = UIBullshit.uiScale;
		int w = Screen.width;
		int h = Screen.height;
		bool scaleChanged = float.IsNaN(_lastScale)
		                    || Mathf.Abs(scale - _lastScale) > 0.0001f
		                    || w != _lastW
		                    || h != _lastH
		                    || recalc;
		bool uiOpen = IsMpUiOpen();
		bool first = !_stamped;

		if (uiOpen || scaleChanged || first)
		{
			if (uiOpen)
				ReasonOpen++;
			else if (scaleChanged)
				ReasonScale++;
			else
				ReasonFirst++;
			_lastScale = scale;
			_lastW = w;
			_lastH = h;
			_stamped = true;
			StampCount++;
			return;
		}

		SkipThisSetSkin = true;
		SkipCount++;
	}

	internal static bool IsMpUiOpen()
	{
		if (UIMainMenu.IsOpen() || UIMainMenu.mainmenu_open)
			return true;
		try
		{
			if (Con.IsConsoleOpen())
				return true;
		}
		catch
		{
		}
		try
		{
			if (GUILayout_DropdownMenu.isOpen)
				return true;
		}
		catch
		{
		}
		if (!KrokoshaScavMultiplayer.IsNetworkActiveAndIsWorldGenerated())
			return true;
		return false;
	}

	internal static void ConsumeProbe(out int stamp, out int skip, out int open, out int scale, out int first)
	{
		stamp = StampCount;
		skip = SkipCount;
		open = ReasonOpen;
		scale = ReasonScale;
		first = ReasonFirst;
		StampCount = 0;
		SkipCount = 0;
		ReasonOpen = 0;
		ReasonScale = 0;
		ReasonFirst = 0;
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
		if (Plugin.UiOptimizationExperimental == null || !Plugin.UiOptimizationExperimental.Value ||
		    Plugin.DeduplicateInGameUi == null || !Plugin.DeduplicateInGameUi.Value)
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
