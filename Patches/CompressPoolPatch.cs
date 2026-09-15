using System.IO;
using System.IO.Compression;
using System.Reflection;
using HarmonyLib;

namespace KrokMPOptimization2;

// reuses compression scratch streams so repeated sync packets create less garbage.
internal static class CompressPool
{
	private static readonly MemoryStream GzipOut = new MemoryStream(4096);
	private static readonly MemoryStream DeflateOut = new MemoryStream(4096);

	internal static bool TryCompressGzip(byte[] data, out byte[] result)
	{
		result = null;
		if (Plugin.CompressPooling == null || !Plugin.CompressPooling.Value || data == null)
			return false;

		GzipOut.Position = 0;
		GzipOut.SetLength(0);
		using (var gz = new GZipStream(GzipOut, CompressionLevel.Optimal, leaveOpen: true))
			gz.Write(data, 0, data.Length);
		result = GzipOut.ToArray();
		MemoryTelemetry.HitGzipPool();
		return true;
	}

	internal static bool TryCompressDeflate(byte[] data, out byte[] result)
	{
		result = null;
		if (Plugin.CompressPooling == null || !Plugin.CompressPooling.Value || data == null)
			return false;

		DeflateOut.Position = 0;
		DeflateOut.SetLength(0);
		using (var deflate = new DeflateStream(DeflateOut, CompressionLevel.Optimal, leaveOpen: true))
			deflate.Write(data, 0, data.Length);
		result = DeflateOut.ToArray();
		MemoryTelemetry.HitDeflatePool();
		return true;
	}
}

[HarmonyPatch]
internal static class CompressPoolGzipPatch
{
	static MethodBase TargetMethod() =>
		AccessTools.Method(AccessTools.TypeByName("KrokoshaCasualtiesUtils.Util"), "Compress", new[] { typeof(byte[]) });

	static bool Prefix(byte[] data, ref byte[] __result)
	{
		return !CompressPool.TryCompressGzip(data, out __result);
	}
}

[HarmonyPatch]
internal static class CompressPoolDeflatePatch
{
	static MethodBase TargetMethod() =>
		AccessTools.Method(AccessTools.TypeByName("KrokoshaCasualtiesUtils.Util"), "CompressDeflate", new[] { typeof(byte[]) });

	static bool Prefix(byte[] data, ref byte[] __result)
	{
		return !CompressPool.TryCompressDeflate(data, out __result);
	}
}
