using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace KrokMPOptimization2;

// resolves and filters the inventory gather hooks used by registry culling.
internal static class InventoryGatherFilter
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
		return _itemGetContainerInfo != null && _iciSurfaceInv != null;
	}

	internal static bool ShouldExcludeFromGather(GameObject go)
	{
		if (go == null)
			return true;

		if (InventoryHelper.IsSurfaceInventoryItem(go) || InventoryHelper.IsOnPlayerBody(go))
			return true;

		if (IsInsideClosedContainer(go))
			return true;

		return false;
	}

	private static bool IsInsideClosedContainer(GameObject go)
	{
		if ((Object)(object)go.transform.parent == (Object)null)
			return false;

		Transform p = go.transform.parent;
		while (p != null)
		{
			if (p.GetComponent<Container>() != null)
				return true;
			p = p.parent;
		}
		return false;
	}
}
