using System;
using System.Threading;
using UnityEngine.Profiling;

namespace KrokMPOptimization2;

// counts allocation and cleanup paths so config changes can be measured in-game.
internal static class MemoryTelemetry
{
	private static long _skinSkip;
	private static long _uiDedup;
	private static long _gzipPool;
	private static long _deflatePool;
	private static long _compressTinySkip;
	private static long _unconsciousFix;
	private static long _objstatePrune;
	private static long _avatarDestroy;
	private static long _texDestroy;
	private static long _limbMatDestroy;
	private static long _coolListFill;
	private static long _snapRent;
	private static long _bitsetPool;
	private static long _packWriterReuse;
	private static long _plrPackSkipClone;

	private static int _skinSkipFirst;
	private static int _uiDedupFirst;
	private static int _gzipPoolFirst;
	private static int _deflatePoolFirst;
	private static int _compressTinySkipFirst;
	private static int _unconsciousFixFirst;
	private static int _coolListFillFirst;
	private static int _snapRentFirst;
	private static int _bitsetPoolFirst;
	private static int _packWriterReuseFirst;
	private static int _plrPackSkipCloneFirst;

	private static int _prevGc0 = GC.CollectionCount(0);
	private static int _prevGc1 = GC.CollectionCount(1);
	private static int _prevGc2 = GC.CollectionCount(2);

	private static float _windowTimer;

	internal static bool Enabled =>
		Plugin.MemoryTelemetryEnabled == null || Plugin.MemoryTelemetryEnabled.Value;

	internal static void HitSkinSkip() => Rate(ref _skinSkip, ref _skinSkipFirst, "skinSkip");
	internal static void HitUiDedup() => Rate(ref _uiDedup, ref _uiDedupFirst, "uiDedup");
	internal static void HitGzipPool() => Rate(ref _gzipPool, ref _gzipPoolFirst, "gzipPool");
	internal static void HitDeflatePool() => Rate(ref _deflatePool, ref _deflatePoolFirst, "deflatePool");
	internal static void HitCompressTinySkip() => Rate(ref _compressTinySkip, ref _compressTinySkipFirst, "compressTinySkip");
	internal static void HitUnconsciousFix() => Rate(ref _unconsciousFix, ref _unconsciousFixFirst, "unconsciousFix");
	internal static void HitCoolListFill() => Rate(ref _coolListFill, ref _coolListFillFirst, "coolListFill");
	internal static void HitSnapRent() => Rate(ref _snapRent, ref _snapRentFirst, "snapRent");
	internal static void HitBitsetPool() => Rate(ref _bitsetPool, ref _bitsetPoolFirst, "bitsetPool");
	internal static void HitPackWriterReuse() => Rate(ref _packWriterReuse, ref _packWriterReuseFirst, "packWriterReuse");
	internal static void HitPlrPackSkipClone() => Rate(ref _plrPackSkipClone, ref _plrPackSkipCloneFirst, "plrPackSkipClone");

	internal static void EventObjstatePrune(int n, string reason) => Event(ref _objstatePrune, n, "objstatePrune", reason);
	internal static void EventAvatarDestroy(int n, string reason) => Event(ref _avatarDestroy, n, "avatarDestroy", reason);
	internal static void EventTexDestroy(int n, string reason) => Event(ref _texDestroy, n, "texDestroy", reason);
	internal static void EventLimbMatDestroy(int n, string reason) => Event(ref _limbMatDestroy, n, "limbMatDestroy", reason);

	private static void Rate(ref long counter, ref int firstFlag, string key)
	{
		if (!Enabled)
			return;
		Interlocked.Increment(ref counter);
		if (Interlocked.CompareExchange(ref firstFlag, 1, 0) == 0)
			OptLog.Info($"[KrokMPOpt2] memory {key} first hit");
	}

	private static void Event(ref long counter, int n, string key, string reason)
	{
		if (n <= 0)
			return;
		Interlocked.Add(ref counter, n);
		if (Enabled)
			OptLog.Info($"[KrokMPOpt2] memory {key} n={n} reason={reason}");
	}

	internal static void Tick(float unscaledDt)
	{
		if (!Enabled)
			return;

		float window = Plugin.TelemetryWindowSeconds?.Value ?? 10f;
		if (window < 1f)
			window = 1f;

		_windowTimer += unscaledDt;
		if (_windowTimer < window)
			return;
		_windowTimer = 0f;
		Flush(window);
	}

	private static void Flush(float windowSec)
	{
		double inv = 1.0 / windowSec;
		long gcMb = GC.GetTotalMemory(false) / (1024 * 1024);
		long monoUsed = SafeMb(Profiler.GetMonoUsedSizeLong);
		long monoHeap = SafeMb(Profiler.GetMonoHeapSizeLong);
		long unityAlloc = SafeMb(Profiler.GetTotalAllocatedMemoryLong);
		int gc0Now = GC.CollectionCount(0);
		int gc1Now = GC.CollectionCount(1);
		int gc2Now = GC.CollectionCount(2);
		int gc0 = gc0Now - _prevGc0;
		int gc1 = gc1Now - _prevGc1;
		int gc2 = gc2Now - _prevGc2;
		_prevGc0 = gc0Now;
		_prevGc1 = gc1Now;
		_prevGc2 = gc2Now;
		long skin = Interlocked.Exchange(ref _skinSkip, 0);
		long dedup = Interlocked.Exchange(ref _uiDedup, 0);
		long gzip = Interlocked.Exchange(ref _gzipPool, 0);
		long deflate = Interlocked.Exchange(ref _deflatePool, 0);
		long tiny = Interlocked.Exchange(ref _compressTinySkip, 0);
		long unconscious = Interlocked.Exchange(ref _unconsciousFix, 0);
		long objstates = Interlocked.Exchange(ref _objstatePrune, 0);
		long avatars = Interlocked.Exchange(ref _avatarDestroy, 0);
		long tex = Interlocked.Exchange(ref _texDestroy, 0);
		long limbs = Interlocked.Exchange(ref _limbMatDestroy, 0);
		long coolList = Interlocked.Exchange(ref _coolListFill, 0);
		long snap = Interlocked.Exchange(ref _snapRent, 0);
		long bitset = Interlocked.Exchange(ref _bitsetPool, 0);
		long packWriter = Interlocked.Exchange(ref _packWriterReuse, 0);
		long plrClone = Interlocked.Exchange(ref _plrPackSkipClone, 0);

		OptLog.Info(
			$"[KrokMPOpt2] memory gcMB={gcMb} monoUsedMB={monoUsed} monoHeapMB={monoHeap} unityAllocMB={unityAlloc} " +
			$"gc0={gc0} gc1={gc1} gc2={gc2} " +
			$"skinSkip={skin * inv:F1}/s uiDedup={dedup * inv:F1}/s " +
			$"gzipPool={gzip * inv:F1}/s deflatePool={deflate * inv:F1}/s compressTinySkip={tiny * inv:F1}/s unconsciousFix={unconscious * inv:F1}/s " +
			$"coolListFill={coolList * inv:F1}/s snapRent={snap * inv:F1}/s bitsetPool={bitset * inv:F1}/s " +
			$"packWriterReuse={packWriter * inv:F1}/s plrPackSkipClone={plrClone * inv:F1}/s " +
			$"objstatePrune={objstates} avatarDestroy={avatars} texDestroy={tex} limbMatDestroy={limbs}");
	}

	private static long SafeMb(Func<long> getBytes)
	{
		try
		{
			return getBytes() / (1024 * 1024);
		}
		catch
		{
			return -1;
		}
	}
}
