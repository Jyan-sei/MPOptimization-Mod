using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace KrokMPOptimization2;

internal static class CatPatchReactivityPatches
{
	internal const string Guid = "meow.catpatch";

	internal static bool Hooked { get; private set; }
	internal static int SkipEmote;
	internal static int SkipHug;
	internal static int SkipKiss;
	internal static int SkipDogpile;
	internal static int SkipPiggy;
	internal static int SkipUpdater;
	internal static int RunSocial;
	internal static int FotBody;
	internal static int SkipOnGui;
	internal static int SkipOverlay;

	private static readonly HashSet<int> FirstRun = new HashSet<int>();
	private static FieldInfo _emoteWheel;
	private static FieldInfo _emoteVisual;
	private static FieldInfo _activeEmotes;
	private static FieldInfo _hugVisual;
	private static FieldInfo _hugPrompt;
	private static FieldInfo _kissVisual;
	private static FieldInfo _kissPrompt;
	private static FieldInfo _bodyA;
	private static PropertyInfo _hasPrompt;
	private static FieldInfo _piggyHandlers;
	private static FieldInfo _settingsWindowOpen;
	private static FieldInfo _moderationWindowOpen;
	private static FieldInfo _sprayPreview;
	private static GUIStyle _cachedKrokButton;
	private static int _cachedStyleScreenW;
	private static int _cachedStyleScreenH;
	private static float _guardAt = -1f;
	private static bool _guardCached;

	internal static void Apply(Harmony harmony)
	{
		if (Plugin.SkipIdleCatPatchControllers == null || !Plugin.SkipIdleCatPatchControllers.Value)
			return;
		if (!BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(Guid))
		{
			OptLog.Info("[KrokMPOpt2] catpatch not loaded - reactivity skips off");
			return;
		}

		try
		{
			int patched = 0;
			patched += PatchUpdate("CatPatchspace.EmoteController", nameof(SkipEmoteUpdate), harmony);
			patched += PatchLate("CatPatchspace.EmoteController", nameof(SkipEmoteLate), harmony);
			patched += PatchUpdate("CatPatchspace.HugController", nameof(SkipHugUpdate), harmony);
			patched += PatchLate("CatPatchspace.HugController", nameof(SkipHugLate), harmony);
			patched += PatchUpdate("CatPatchspace.KissController", nameof(SkipKissUpdate), harmony);
			patched += PatchLate("CatPatchspace.KissController", nameof(SkipKissLate), harmony);
			patched += PatchUpdate("CatPatchspace.DogpileController", nameof(SkipDogpileUpdate), harmony);
			patched += PatchUpdate("CatPatchspace.PiggybackThrowController", nameof(SkipPiggyUpdate), harmony);
			patched += PatchUpdate("CatPatchspace.CatPatchUpdater", nameof(SkipUpdaterUpdate), harmony);
			patched += PatchNamed("CatPatchspace.CatPatchHostSettingsMenu", "OnGUI", nameof(SkipSettingsOnGui), harmony);
			patched += PatchNamed("CatPatchspace.CatPatchHostSettingsMenu", "DrawOverlayFromKrokMenu", nameof(SkipSettingsOverlay), harmony);
			patched += PatchNamed("CatPatchspace.CatPatchHostModerationMenu", "OnGUI", nameof(SkipModerationOnGui), harmony);
			patched += PatchNamed("CatPatchspace.CatPatchHostModerationMenu", "DrawMenuButtonFromKrokMenu", nameof(SkipModerationOverlay), harmony);
			patched += PatchNamed("CatPatchspace.EmoteController", "OnGUI", nameof(SkipEmoteOnGui), harmony);
			patched += PatchNamed("CatPatchspace.HugController", "OnGUI", nameof(SkipHugOnGui), harmony);
			patched += PatchNamed("CatPatchspace.KissController", "OnGUI", nameof(SkipKissOnGui), harmony);
			patched += PatchNamed("CatPatchspace.SprayInspector", "OnGUI", nameof(SkipSprayOnGui), harmony);
			patched += PatchNamed("CatPatchspace.CatPatchUpdater", "OnGUI", nameof(SkipUpdaterOnGui), harmony);
			patched += PatchStyleCache("CatPatchspace.CatPatchHostSettingsMenu", harmony);
			patched += PatchStyleCache("CatPatchspace.SprayInspector", harmony);

			var guard = AccessTools.TypeByName("CatPatchspace.CatPatchConsoleGuard");
			var guardMethod = guard != null ? AccessTools.Method(guard, "IsGameInputUiActive") : null;
			if (guardMethod != null)
			{
				harmony.Patch(guardMethod, prefix: new HarmonyMethod(typeof(CatPatchReactivityPatches), nameof(FastConsoleGuard)));
				patched++;
			}

			ResolveFields();
			Hooked = patched > 0;
			OptLog.Info($"[KrokMPOpt2] catpatch reactivity hooked={ (Hooked ? 1 : 0) } patches={patched}");
		}
		catch (Exception ex)
		{
			Hooked = false;
			OptLog.Warn($"[KrokMPOpt2] catpatch reactivity failed: {ex.Message}");
		}
	}

	static int PatchUpdate(string typeName, string prefix, Harmony harmony)
	{
		var t = AccessTools.TypeByName(typeName);
		var m = t != null ? AccessTools.Method(t, "Update") : null;
		if (m == null)
			return 0;
		harmony.Patch(m, prefix: new HarmonyMethod(typeof(CatPatchReactivityPatches), prefix));
		return 1;
	}

	static int PatchNamed(string typeName, string method, string prefix, Harmony harmony)
	{
		var t = AccessTools.TypeByName(typeName);
		var m = t != null ? AccessTools.Method(t, method) : null;
		if (m == null)
			return 0;
		harmony.Patch(m, prefix: new HarmonyMethod(typeof(CatPatchReactivityPatches), prefix));
		return 1;
	}

	static int PatchStyleCache(string typeName, Harmony harmony)
	{
		var t = AccessTools.TypeByName(typeName);
		var m = t != null ? AccessTools.Method(t, "CreateKrokStyleButton") : null;
		if (m == null)
			return 0;
		harmony.Patch(
			m,
			prefix: new HarmonyMethod(typeof(CatPatchReactivityPatches), nameof(CacheKrokButtonPrefix)),
			postfix: new HarmonyMethod(typeof(CatPatchReactivityPatches), nameof(CacheKrokButtonPostfix)));
		return 1;
	}

	static int PatchLate(string typeName, string prefix, Harmony harmony)
	{
		var t = AccessTools.TypeByName(typeName);
		var m = t != null ? AccessTools.Method(t, "LateUpdate") : null;
		if (m == null)
			return 0;
		harmony.Patch(m, prefix: new HarmonyMethod(typeof(CatPatchReactivityPatches), prefix));
		return 1;
	}

	static void ResolveFields()
	{
		const BindingFlags inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
		const BindingFlags stat = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
		var emote = AccessTools.TypeByName("CatPatchspace.EmoteController");
		var emoteVis = AccessTools.TypeByName("CatPatchspace.EmoteVisualController");
		var hug = AccessTools.TypeByName("CatPatchspace.HugController");
		var hugVis = AccessTools.TypeByName("CatPatchspace.HugVisualController");
		var hugPrompt = AccessTools.TypeByName("CatPatchspace.HugPromptState");
		var kiss = AccessTools.TypeByName("CatPatchspace.KissController");
		var kissVis = AccessTools.TypeByName("CatPatchspace.KissVisualController");
		var kissPrompt = AccessTools.TypeByName("CatPatchspace.KissPromptState");
		var piggy = AccessTools.TypeByName("CatPatchspace.PiggybackThrowController");

		_emoteWheel = emote?.GetField("_wheelOpen", inst);
		_emoteVisual = emote?.GetField("_visualController", inst);
		_activeEmotes = emoteVis?.GetField("_activeEmotesByClientId", inst);
		_hugVisual = hug?.GetField("_visualController", inst);
		_hugPrompt = hug?.GetField("_promptState", inst);
		_kissVisual = kiss?.GetField("_visualController", inst);
		_kissPrompt = kiss?.GetField("_promptState", inst);
		_bodyA = hugVis?.GetField("_bodyA", inst) ?? kissVis?.GetField("_bodyA", inst);
		_hasPrompt = hugPrompt?.GetProperty("HasIncomingPrompt") ?? kissPrompt?.GetProperty("HasIncomingPrompt");
		_piggyHandlers = piggy?.GetField("_handlersRegistered", stat);
		if (_bodyA == null && kissVis != null)
			_bodyA = kissVis.GetField("_bodyA", inst);

		var settings = AccessTools.TypeByName("CatPatchspace.CatPatchHostSettingsMenu");
		var moderation = AccessTools.TypeByName("CatPatchspace.CatPatchHostModerationMenu");
		var spray = AccessTools.TypeByName("CatPatchspace.SprayInspector");
		_settingsWindowOpen = settings?.GetField("_windowOpen", stat);
		_moderationWindowOpen = moderation?.GetField("_windowOpen", stat);
		_sprayPreview = spray?.GetField("_previewSpray", inst);
	}

	static bool AllowFirst(MonoBehaviour mb)
	{
		if (!mb)
			return false;
		int id = mb.GetInstanceID();
		if (FirstRun.Add(id))
		{
			RunSocial++;
			return true;
		}
		return false;
	}

	static bool IdleSocial(MonoBehaviour mb, FieldInfo visualField, FieldInfo promptField, FieldInfo bodyFieldOnVisual)
	{
		if (AllowFirst(mb))
			return false;
		try
		{
			object visual = visualField?.GetValue(mb);
			object prompt = promptField?.GetValue(mb);
			Body bodyA = null;
			if (visual != null)
			{
				FieldInfo bodyField = visual.GetType().GetField("_bodyA", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				bodyA = bodyField?.GetValue(visual) as Body;
			}
			bool promptOn = false;
			if (prompt != null)
			{
				PropertyInfo promptProp = prompt.GetType().GetProperty("HasIncomingPrompt");
				promptOn = promptProp?.GetValue(prompt) is true;
			}
			if (bodyA == null && !promptOn)
				return true;
		}
		catch
		{
		}
		RunSocial++;
		return false;
	}

	static bool SkipEmoteUpdate(MonoBehaviour __instance)
	{
		if (AllowFirst(__instance))
			return true;
		try
		{
			bool wheel = _emoteWheel != null && _emoteWheel.GetValue(__instance) is true;
			object visual = _emoteVisual?.GetValue(__instance);
			var dict = visual != null ? _activeEmotes?.GetValue(visual) as IDictionary : null;
			if (!wheel && (dict == null || dict.Count == 0))
			{
				SkipEmote++;
				return false;
			}
		}
		catch
		{
		}
		RunSocial++;
		return true;
	}

	static bool SkipEmoteLate(MonoBehaviour __instance) => SkipEmoteUpdate(__instance);

	static bool SkipHugUpdate(MonoBehaviour __instance)
	{
		if (IdleSocial(__instance, _hugVisual, _hugPrompt, _bodyA))
		{
			SkipHug++;
			return false;
		}
		return true;
	}

	static bool SkipHugLate(MonoBehaviour __instance) => SkipHugUpdate(__instance);

	static bool SkipKissUpdate(MonoBehaviour __instance)
	{
		if (IdleSocial(__instance, _kissVisual, _kissPrompt, _bodyA))
		{
			SkipKiss++;
			return false;
		}
		return true;
	}

	static bool SkipKissLate(MonoBehaviour __instance) => SkipKissUpdate(__instance);

	static bool SkipDogpileUpdate()
	{
		try
		{
			if (AnyLivingPlayerSleeping())
			{
				FotBody++;
				return true;
			}
		}
		catch
		{
		}
		SkipDogpile++;
		return false;
	}

	static bool AnyLivingPlayerSleeping()
	{
		var living = NetPlayer.AllLivingPlayers;
		if (living == null)
			return false;
		foreach (var plr in living)
		{
			if (plr?.body != null && plr.body.sleeping)
				return true;
		}
		return false;
	}

	static bool SkipPiggyUpdate(MonoBehaviour __instance)
	{
		if (AllowFirst(__instance))
			return true;
		try
		{
			if (_piggyHandlers != null && _piggyHandlers.GetValue(null) is false)
				return true;
			NetPlayer local = NetPlayer.LOCAL_PLAYER;
			NetBody pb = local != null ? local.playerbody : null;
			if (pb == null || pb.carrying_person == null)
			{
				SkipPiggy++;
				return false;
			}
		}
		catch
		{
		}
		RunSocial++;
		return true;
	}

	static bool SkipUpdaterUpdate()
	{
		SkipUpdater++;
		return false;
	}

	static bool IsPlayerInWorld()
	{
		try { return PlayerCamera.main != null; } catch { return false; }
	}

	static bool IsSettingsWindowOpen()
	{
		try { return _settingsWindowOpen != null && _settingsWindowOpen.GetValue(null) is true; } catch { return false; }
	}

	static bool IsModerationWindowOpen()
	{
		try { return _moderationWindowOpen != null && _moderationWindowOpen.GetValue(null) is true; } catch { return false; }
	}

	static bool IsAnyCatPatchWindowOpen() => IsSettingsWindowOpen() || IsModerationWindowOpen();

	static bool SkipSettingsOnGui()
	{
		if (IsSettingsWindowOpen()) { SkipOnGui++; return true; }
		if (!IsPlayerInWorld()) return true;
		SkipOnGui++;
		return false;
	}

	static bool SkipModerationOnGui()
	{
		if (IsModerationWindowOpen()) { SkipOnGui++; return true; }
		SkipOnGui++;
		return false;
	}

	static bool SkipSettingsOverlay()
	{
		if (IsSettingsWindowOpen()) { SkipOverlay++; return true; }
		if (!IsPlayerInWorld()) return true;
		SkipOverlay++;
		return false;
	}

	static bool SkipModerationOverlay()
	{
		if (IsModerationWindowOpen()) { SkipOverlay++; return true; }
		if (!IsPlayerInWorld()) return true;
		SkipOverlay++;
		return false;
	}

	static bool SkipEmoteOnGui(MonoBehaviour __instance)
	{
		try
		{
			bool wheel = _emoteWheel != null && _emoteWheel.GetValue(__instance) is true;
			if (wheel) { SkipOnGui++; return true; }
			object visual = _emoteVisual?.GetValue(__instance);
			var dict = visual != null ? _activeEmotes?.GetValue(visual) as IDictionary : null;
			if (dict != null && dict.Count > 0) { SkipOnGui++; return true; }
		}
		catch { }
		SkipOnGui++;
		return false;
	}

	static bool SkipHugOnGui(MonoBehaviour __instance)
	{
		try
		{
			object visual = _hugVisual?.GetValue(__instance);
			Body bodyA = visual != null ? _bodyA?.GetValue(visual) as Body : null;
			object prompt = _hugPrompt?.GetValue(__instance);
			bool promptOn = prompt != null && _hasPrompt != null && _hasPrompt.GetValue(prompt) is true;
			if (bodyA != null || promptOn) { SkipOnGui++; return true; }
		}
		catch { }
		SkipOnGui++;
		return false;
	}

	static bool SkipKissOnGui(MonoBehaviour __instance)
	{
		try
		{
			object visual = _kissVisual?.GetValue(__instance);
			Body bodyA = visual != null ? _bodyA?.GetValue(visual) as Body : null;
			object prompt = _kissPrompt?.GetValue(__instance);
			bool promptOn = prompt != null && _hasPrompt != null && _hasPrompt.GetValue(prompt) is true;
			if (bodyA != null || promptOn) { SkipOnGui++; return true; }
		}
		catch { }
		SkipOnGui++;
		return false;
	}

	static bool SkipSprayOnGui(MonoBehaviour __instance)
	{
		if (_sprayPreview != null && _sprayPreview.GetValue(__instance) != null) { SkipOnGui++; return true; }
		SkipOnGui++;
		return false;
	}

	static bool SkipUpdaterOnGui()
	{
		SkipOnGui++;
		return false;
	}

	static bool CacheKrokButtonPrefix(ref GUIStyle __result)
	{
		if (_cachedKrokButton != null && Screen.width == _cachedStyleScreenW && Screen.height == _cachedStyleScreenH)
		{
			__result = _cachedKrokButton;
			return false;
		}
		return true;
	}

	static void CacheKrokButtonPostfix(GUIStyle __result)
	{
		if (__result != null)
		{
			_cachedKrokButton = __result;
			_cachedStyleScreenW = Screen.width;
			_cachedStyleScreenH = Screen.height;
		}
	}

	static bool FastConsoleGuard(ref bool __result)
	{
		float now = Time.unscaledTime;
		if (now - _guardAt < 0.15f)
		{
			__result = _guardCached;
			return false;
		}
		_guardAt = now;
		bool open = false;
		try
		{
			open = Con.IsConsoleOpen();
		}
		catch
		{
		}
		try
		{
			PlayerCamera cam = PlayerCamera.main;
			if (cam != null && cam.craftingPanel != null && cam.craftingPanel.activeInHierarchy)
				open = true;
		}
		catch
		{
		}
		_guardCached = open;
		__result = open;
		return false;
	}

	internal static void ConsumeProbe(
		out int skipEmote, out int skipHug, out int skipKiss, out int skipDogpile,
		out int skipPiggy, out int skipUpdater, out int runSocial, out int fotBody,
		out int skipOnGui, out int skipOverlay)
	{
		skipEmote = SkipEmote;
		skipHug = SkipHug;
		skipKiss = SkipKiss;
		skipDogpile = SkipDogpile;
		skipPiggy = SkipPiggy;
		skipUpdater = SkipUpdater;
		runSocial = RunSocial;
		fotBody = FotBody;
		skipOnGui = SkipOnGui;
		skipOverlay = SkipOverlay;
		SkipEmote = 0;
		SkipHug = 0;
		SkipKiss = 0;
		SkipDogpile = 0;
		SkipPiggy = 0;
		SkipUpdater = 0;
		RunSocial = 0;
		FotBody = 0;
		SkipOnGui = 0;
		SkipOverlay = 0;
	}
}
