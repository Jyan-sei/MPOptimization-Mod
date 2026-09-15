using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace KrokMPOptimization2;

[HarmonyPatch(typeof(PlayerCamera), "HandleUnconsciousScreen")]
// replaces repeated unconscious-screen string concatenation with a fixed value.
internal static class UnconsciousTextPatch
{
	private static readonly string FixedText = "!!!";

	static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		var codes = new List<CodeInstruction>(instructions);
		MethodInfo helper = AccessTools.Method(typeof(UnconsciousTextPatch), nameof(SetFixedText));

		for (int i = 0; i < codes.Count; i++)
		{
			if (codes[i].opcode != OpCodes.Ldstr || codes[i].operand as string != "!")
				continue;
			if (i < 2 || i + 2 >= codes.Count)
				continue;
			if (codes[i - 1].opcode != OpCodes.Dup && !IsGetText(codes[i - 1]))
				continue;
			if (!IsConcat(codes[i + 1]) || !IsSetText(codes[i + 2]))
				continue;

			int start = i - 1;
			if (codes[start].opcode == OpCodes.Dup)
			{
				codes[start] = new CodeInstruction(OpCodes.Call, helper);
				codes[start + 1] = new CodeInstruction(OpCodes.Nop);
				codes[start + 2] = new CodeInstruction(OpCodes.Nop);
				codes[start + 3] = new CodeInstruction(OpCodes.Nop);
			}
			break;
		}

		return codes;
	}

	private static bool IsGetText(CodeInstruction ins) =>
		ins.opcode == OpCodes.Callvirt && ins.operand is MethodInfo m && m.Name == "get_text";

	private static bool IsSetText(CodeInstruction ins) =>
		ins.opcode == OpCodes.Callvirt && ins.operand is MethodInfo m && m.Name == "set_text";

	private static bool IsConcat(CodeInstruction ins) =>
		(ins.opcode == OpCodes.Call || ins.opcode == OpCodes.Callvirt)
		&& ins.operand is MethodInfo m
		&& m.Name == "Concat";

	private static void SetFixedText(Object tmp)
	{
		if (Plugin.FixUnconsciousText == null || !Plugin.FixUnconsciousText.Value)
			return;
		ApplyFixed(tmp);
	}

	static void Postfix(PlayerCamera __instance)
	{
		if (Plugin.FixUnconsciousText == null || !Plugin.FixUnconsciousText.Value)
			return;
		if (__instance?.body == null || !__instance.body.brainDying)
			return;
		object tmp = AccessTools.Field(typeof(PlayerCamera), "consciousnessText")?.GetValue(__instance);
		ApplyFixed(tmp as Object);
	}

	private static void ApplyFixed(Object tmp)
	{
		if (tmp == null)
			return;
		PropertyInfo text = AccessTools.Property(tmp.GetType(), "text");
		if (text == null)
			return;
		if (text.GetValue(tmp) as string != FixedText)
			text.SetValue(tmp, FixedText);
		MemoryTelemetry.HitUnconsciousFix();
	}
}
