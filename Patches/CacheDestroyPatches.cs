using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace KrokMPOptimization2;

// releases cached Steam, UI, and limb resources when their owning objects leave.
internal static class SteamAvatarPrune
{
	private static bool _attached;

	internal static void Attach()
	{
		if (_attached)
			return;
		NetPlayer.OnPlayerLeft += OnPlayerLeft;
		_attached = true;
	}

	internal static void Detach()
	{
		if (!_attached)
			return;
		NetPlayer.OnPlayerLeft -= OnPlayerLeft;
		_attached = false;
	}

	internal static void OnPlayerLeft(NetPlayer leaving)
	{
		OnPlayerDestroyed(leaving, "playerLeft");
		DestroyUnreferenced("playerLeft");
	}

	internal static void OnPlayerDestroyed(NetPlayer leaving, string reason = "playerDestroy")
	{
		if (Plugin.PruneSteamAvatars == null || !Plugin.PruneSteamAvatars.Value)
			return;
		if (leaving == null)
			return;

		int n = 0;
		n += MaybeDestroy(leaving.profilepic_largeicon, leaving);
		n += MaybeDestroy(leaving.profilepic_mediumicon, leaving);
		n += MaybeDestroy(leaving.profilepic_smallicon, leaving);
		if (n > 0)
			MemoryTelemetry.EventAvatarDestroy(n, reason);
	}

	internal static void DestroyUnreferenced(string reason)
	{
		if (Plugin.PruneSteamAvatars == null || !Plugin.PruneSteamAvatars.Value)
			return;

		int n = 0;
		var keys = new List<int>(KSteam.SteamImagesCache.Count);
		foreach (var kv in KSteam.SteamImagesCache)
			keys.Add(kv.Key);
		for (int i = 0; i < keys.Count; i++)
		{
			if (!KSteam.SteamImagesCache.TryGetValue(keys[i], out Texture2D tex) || tex == null)
				continue;
			if (StillReferenced(tex, null))
				continue;
			KSteam.SteamImagesCache.Remove(keys[i]);
			Object.Destroy(tex);
			n++;
		}

		if (n > 0)
			MemoryTelemetry.EventAvatarDestroy(n, reason);
	}

	internal static void DestroyAll(string reason)
	{
		if (Plugin.PruneSteamAvatars == null || !Plugin.PruneSteamAvatars.Value)
			return;

		int n = 0;
		var keys = new List<int>(KSteam.SteamImagesCache.Count);
		foreach (var kv in KSteam.SteamImagesCache)
			keys.Add(kv.Key);
		for (int i = 0; i < keys.Count; i++)
		{
			if (!KSteam.SteamImagesCache.TryGetValue(keys[i], out Texture2D tex))
				continue;
			KSteam.SteamImagesCache.Remove(keys[i]);
			if (tex != null)
			{
				Object.Destroy(tex);
				n++;
			}
		}

		if (n > 0)
			MemoryTelemetry.EventAvatarDestroy(n, reason);
	}

	private static int MaybeDestroy(Texture2D tex, NetPlayer except)
	{
		if (tex == null)
			return 0;
		if (StillReferenced(tex, except))
			return 0;

		var remove = new List<int>(4);
		foreach (var kv in KSteam.SteamImagesCache)
		{
			if (kv.Value == tex)
				remove.Add(kv.Key);
		}

		for (int i = 0; i < remove.Count; i++)
			KSteam.SteamImagesCache.Remove(remove[i]);
		Object.Destroy(tex);
		return 1;
	}

	private static bool StillReferenced(Texture2D tex, NetPlayer except)
	{
		try
		{
			foreach (NetPlayer plr in NetPlayer.AllLivingPlayers)
			{
				if (plr == null || plr == except)
					continue;
				if (plr.profilepic_largeicon == tex
				    || plr.profilepic_mediumicon == tex
				    || plr.profilepic_smallicon == tex)
					return true;
			}
		}
		catch
		{
			// ignore
		}

		return false;
	}
}

[HarmonyPatch(typeof(CoolSyncManager), "OnTransportEnd")]
internal static class MemoryTransportEndPatch
{
	static void Postfix()
	{
		ObjStatePrune.WipeAll("transportEnd");
		SteamAvatarPrune.DestroyAll("transportEnd");
	}
}

[HarmonyPatch(typeof(NetPlayer), nameof(NetPlayer.OnDestroy))]
internal static class SteamAvatarOnDestroyPatch
{
	static void Postfix()
	{
		// OnPlayerLeft normally ran during OnDestroy, so this is a fallback sweep.
		SteamAvatarPrune.DestroyUnreferenced("playerDestroy");
	}
}

[HarmonyPatch(typeof(UIBullshit), nameof(UIBullshit.ResizeGUITextures))]
internal static class UiTextureDestroyPatch
{
	private static FieldInfo _modified;
	private static FieldInfo _scaled;

	static void Prefix()
	{
		if (Plugin.DestroyUiTexturesOnResize == null || !Plugin.DestroyUiTexturesOnResize.Value)
			return;

		_modified ??= AccessTools.Field(typeof(UIBullshit), "cached_modified_textures");
		_scaled ??= AccessTools.Field(typeof(UIBullshit), "cached_scaled_textures");

		int n = 0;
		n += DestroyModified(_modified?.GetValue(null) as IDictionary);
		n += DestroyScaled(_scaled?.GetValue(null) as IDictionary);
		if (n > 0)
			MemoryTelemetry.EventTexDestroy(n, "resize");
	}

	private static int DestroyModified(IDictionary nested)
	{
		if (nested == null)
			return 0;
		int n = 0;
		foreach (DictionaryEntry outer in nested)
		{
			if (outer.Value is not IDictionary inner)
				continue;
			foreach (DictionaryEntry e in inner)
			{
				if (e.Value is Texture2D tex && tex != null)
				{
					Object.Destroy(tex);
					n++;
				}
			}
		}

		return n;
	}

	private static int DestroyScaled(IDictionary dict)
	{
		if (dict == null)
			return 0;
		int n = 0;
		foreach (DictionaryEntry e in dict)
		{
			if (e.Value is Texture2D tex && tex != null)
			{
				Object.Destroy(tex);
				n++;
			}
		}

		return n;
	}
}

[HarmonyPatch(typeof(Limb), "Awake")]
internal static class LimbMaterialAwakePatch
{
	static void Postfix(Limb __instance)
	{
		if (Plugin.DestroyLimbMaterials == null || !Plugin.DestroyLimbMaterials.Value)
			return;
		if (__instance == null)
			return;
		if (__instance.GetComponent<LimbMatDestroyHook>() != null)
			return;
		__instance.gameObject.AddComponent<LimbMatDestroyHook>();
	}
}

internal sealed class LimbMatDestroyHook : MonoBehaviour
{
	private static FieldInfo _mat;

	private void OnDestroy()
	{
		if (Plugin.DestroyLimbMaterials == null || !Plugin.DestroyLimbMaterials.Value)
			return;

		_mat ??= AccessTools.Field(typeof(Limb), "mat");
		var limb = GetComponent<Limb>();
		if (limb == null || _mat == null)
			return;
		if (_mat.GetValue(limb) is not Material mat || mat == null)
			return;
		Object.Destroy(mat);
		_mat.SetValue(limb, null);
		MemoryTelemetry.EventLimbMatDestroy(1, "limbDestroy");
	}
}

[HarmonyPatch(typeof(CharStatusVisuals), nameof(CharStatusVisuals.MakeNametagFancy))]
internal static class NametagFancyMaterialPatch
{
	private static Material _fancy;
	private static FieldInfo _nametag;

	static bool Prefix(CharStatusVisuals __instance)
	{
		if (_fancy == null || __instance == null)
			return true;

		_nametag ??= AccessTools.Field(typeof(CharStatusVisuals), "nametag");
		if (_nametag?.GetValue(__instance) is not GameObject go || go == null)
			return true;

		var tmp = go.GetComponent("TextMeshPro");
		if (tmp == null)
			return true;

		PropertyInfo fontMat = AccessTools.Property(tmp.GetType(), "fontMaterial");
		if (fontMat == null)
			return true;
		fontMat.SetValue(tmp, _fancy);
		return false;
	}

	static void Postfix(CharStatusVisuals __instance)
	{
		if (_fancy != null || __instance == null)
			return;

		_nametag ??= AccessTools.Field(typeof(CharStatusVisuals), "nametag");
		if (_nametag?.GetValue(__instance) is not GameObject go || go == null)
			return;
		var tmp = go.GetComponent("TextMeshPro");
		if (tmp == null)
			return;
		PropertyInfo fontMat = AccessTools.Property(tmp.GetType(), "fontMaterial");
		if (fontMat?.GetValue(tmp) is Material mat)
			_fancy = mat;
	}
}
