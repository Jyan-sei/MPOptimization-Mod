using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace KrokMPOptimization2;

// keeps inventory and world-item checks in one place for host and client cleanup.
internal static class InventoryHelper
{
	private static MethodInfo _itemGetContainerInfo;
	private static FieldInfo _iciSurfaceInv;
	private static FieldInfo _iciBodyNpc;

	internal static bool TryResolve()
	{
		var itemSync = AccessTools.TypeByName("KrokoshaCasualtiesMP.ItemSync");
		var iciType = itemSync != null ? AccessTools.Inner(itemSync, "ItemsContainerInfo") : null;
		_itemGetContainerInfo = itemSync != null
			? AccessTools.Method(itemSync, "ItemGetContainerInfo", new[] { typeof(GameObject) })
			: null;
		_iciSurfaceInv = iciType != null ? AccessTools.Field(iciType, "is_in_surface_inv") : null;
		_iciBodyNpc = iciType != null ? AccessTools.Field(iciType, "bodynpc") : null;
		return _itemGetContainerInfo != null && _iciSurfaceInv != null && _iciBodyNpc != null;
	}

	internal static bool IsInventoryItem(knetid netId)
	{
		if (!NetObjectRegistry.TryGetSyncInfo(netId, out SyncInfo si) || (Object)(object)si.go == (Object)null)
			return false;
		return IsOnPlayerBody(si.go) || IsSurfaceInventoryItem(si.go);
	}

	internal static bool IsOnPlayerBody(GameObject go)
	{
		if (go == null || go.GetComponent("Item") == null)
			return false;
		var ici = GetContainerInfo(go);
		if (ici == null)
			return false;
		var bodyNpc = _iciBodyNpc.GetValue(ici);
		return bodyNpc is NetBody nb && nb.is_player;
	}

	internal static bool IsSurfaceInventoryItem(GameObject go)
	{
		if (go == null || go.GetComponent("Item") == null)
			return false;
		var ici = GetContainerInfo(go);
		return ici != null && _iciSurfaceInv.GetValue(ici) is true;
	}

	internal static bool IsWorldItem(GameObject go)
	{
		if (go == null || go.GetComponent("Item") == null)
			return false;
		if ((Object)(object)go.transform.parent == (Object)null)
			return true;

		var ici = GetContainerInfo(go);
		if (ici == null)
			return false;
		if (_iciSurfaceInv.GetValue(ici) is true)
			return false;
		var bodyNpc = _iciBodyNpc.GetValue(ici);
		return bodyNpc == null;
	}

	internal static bool IsPlayerInventoryItem(GameObject go, NetBody localBody)
	{
		if (go == null || go.GetComponent("Item") == null)
			return false;
		var ici = GetContainerInfo(go);
		if (ici == null)
			return false;
		if (_iciSurfaceInv.GetValue(ici) is true)
			return true;
		var bodyNpc = _iciBodyNpc.GetValue(ici);
		return ReferenceEquals(bodyNpc, localBody);
	}

	private static object GetContainerInfo(GameObject go)
	{
		try
		{
			return _itemGetContainerInfo?.Invoke(null, new object[] { go });
		}
		catch
		{
			return null;
		}
	}
}
