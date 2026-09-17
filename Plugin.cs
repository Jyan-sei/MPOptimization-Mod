using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using KrokMPOptimization2.ClientRelief;
using UnityEngine;

namespace KrokMPOptimization2;

[BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
[BepInDependency("KrokoshaCasualtiesMP", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("meow.catpatch", BepInDependency.DependencyFlags.SoftDependency)]
// combines host sync scheduling, queue protection, client relief, and memory cleanup.
public class Plugin : BaseUnityPlugin
{
	internal const string V1Guid = "com.local.krokmp.optimization";

	internal static ManualLogSource Log;

	internal static ConfigEntry<bool> Enabled;
	internal static ConfigEntry<int> QueueCap;
	internal static ConfigEntry<float> QueueOverflowFraction;
	internal static ConfigEntry<float> HeadPinTimeoutSeconds;
	internal static ConfigEntry<float> StaticPinTimeoutSeconds;
	internal static ConfigEntry<float> ForceSyncHeadTimeoutSeconds;
	internal static ConfigEntry<int> HeadPinMaxEvictPerPlayerPerTick;
	internal static ConfigEntry<int> ForceSyncMaxEvictPerPlayerPerTick;
	internal static ConfigEntry<float> GcSliceMs;
	internal static ConfigEntry<bool> SkipUnchangedObjects;
	internal static ConfigEntry<int> JoinQueueCap;
	internal static ConfigEntry<float> JoinQueueCapGraceSeconds;
	internal static ConfigEntry<int> AckDrainMaxPerReceive;

	internal static ConfigEntry<int> HotLaneCapPerTick;
	internal static ConfigEntry<int> NearLaneCapPerTick;
	internal static ConfigEntry<int> DormantLaneCapPerTick;
	internal static ConfigEntry<float> SafetyNetRadius;
	internal static ConfigEntry<float> SafetyNetHz;
	internal static ConfigEntry<int> SafetyNetMaxPerSweep;
	internal static ConfigEntry<float> CullDistance;
	internal static ConfigEntry<float> CullIntervalSeconds;
	internal static ConfigEntry<int> JoinBurstMax;
	internal static ConfigEntry<float> AutoRegisterRadius;
	internal static ConfigEntry<bool> ContainerPrioritySyncEnabled;

	internal static ConfigEntry<bool> DebugLog;
	internal static ConfigEntry<float> TelemetryWindowSeconds;
	internal static ConfigEntry<bool> VerboseLogging;
	internal static ConfigEntry<bool> FrameTimingEnabled;
	internal static ConfigEntry<bool> FrameTimingCsvEnabled;

	internal static ConfigEntry<bool> ClientReliefEnabled;
	internal static ConfigEntry<int> ClientReliefMaxMessagesPerFrame;
	internal static ConfigEntry<int> ClientReliefPollBufferSize;
	internal static ConfigEntry<bool> ClientReliefLazyIdDicts;
	internal static ConfigEntry<bool> ClientReliefLocalFpsScaleEnabled;
	internal static ConfigEntry<float> ClientReliefLocalFpsTargetMs;
	internal static ConfigEntry<float> ClientReliefLocalFpsMinScale;
	internal static ConfigEntry<bool> ClientReliefRegistryThrottleEnabled;
	internal static ConfigEntry<float> ClientReliefRegistryThrottleScaleThreshold;

	internal static ConfigEntry<bool> SkipIdleSkinRebuild;
		internal static ConfigEntry<bool> DeduplicateInGameUi;
	internal static ConfigEntry<bool> UiOptimizationExperimental;
		internal static ConfigEntry<bool> CompressPooling;
	internal static ConfigEntry<bool> FixUnconsciousText;
	internal static ConfigEntry<bool> PruneCoolSyncObjStates;
	internal static ConfigEntry<bool> PruneSteamAvatars;
	internal static ConfigEntry<bool> CoolSyncAllocPooling;
	internal static ConfigEntry<bool> CoolSyncWriterPool;
	internal static ConfigEntry<float> HeapProbeSeconds;
	internal static ConfigEntry<KeyCode> HeapProbeKey;
	internal static ConfigEntry<bool> HeapProbeForceCollect;
	internal static ConfigEntry<float> ObjStatePruneTickSeconds;
	internal static ConfigEntry<bool> DestroyUiTexturesOnResize;
	internal static ConfigEntry<bool> DestroyLimbMaterials;
	internal static ConfigEntry<bool> MemoryTelemetryEnabled;

	internal static ConfigEntry<bool> ElderHiFiEnabled;
	internal static ConfigEntry<int> ElderHiFiRateHz;
	internal static ConfigEntry<float> ElderHiFiRadius;
	internal static ConfigEntry<bool> SkipIdleCatPatchControllers;

	internal static bool PinTimeoutSessionDisabled;
	internal static bool StaticPinTimeoutSessionDisabled;

	internal static bool IsHostSessionActive =>
		Net.running && Net.is_server;

	internal static bool ClientReliefActive =>
		ClientReliefEnabled != null && ClientReliefEnabled.Value;

	private Harmony _harmony;
	private SyncProducerHost _producerHost;
	private bool _hostArmed;
	private float _probeAge;

	private void Awake()
	{
		Log = Logger;
		OptLog.Info($"[KrokMPOpt2] v{PluginInfo.Version}");

		if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(V1Guid))
		{
			Logger.LogError(
				"[KrokMPOpt2] KrokMPOptimization v1 is loaded - remove BepInEx/plugins/KrokMPOptimization/KrokMPOptimization.dll before using v2.");
			return;
		}

		Enabled = Config.Bind("General", "Enabled", true, "master switch for host-only SyncProducer and queue patches");

		ClientReliefEnabled = Config.Bind("ClientRelief", "Enabled", true,
			"client-side transport/sync relief (msg budget, lazy id dicts, local fps scale). ok on host too");
		ClientReliefMaxMessagesPerFrame = Config.Bind("ClientRelief", "MaxMessagesPerFrame", 256,
			"max steam packets processed per frame on clients");
		ClientReliefPollBufferSize = Config.Bind("ClientRelief", "PollBufferSize", 512,
			"reused ReceiveMessages buffer on clients (stock allocates 65535 entries every frame)");
		ClientReliefLazyIdDicts = Config.Bind("ClientRelief", "LazyIdDicts", true,
			"skip rebuilding steamid maps when player count unchanged");
		ClientReliefLocalFpsScaleEnabled = Config.Bind("ClientRelief", "LocalFpsScaleEnabled", true,
			"scale client sync timers by local fps, not just host fps");
		ClientReliefLocalFpsTargetMs = Config.Bind("ClientRelief", "LocalFpsTargetMs", 33f,
			"target frame ms for local fps scale (33 ≈ 30 fps)");
		ClientReliefLocalFpsMinScale = Config.Bind("ClientRelief", "LocalFpsMinScale", 0.05f,
			"minimum AdaptiveSync scale when client frame time is high");
		ClientReliefRegistryThrottleEnabled = Config.Bind("ClientRelief", "RegistryThrottleEnabled", true,
			"skip some netobjectregistry client polls when local fps scale is low");
		ClientReliefRegistryThrottleScaleThreshold = Config.Bind("ClientRelief", "RegistryThrottleScaleThreshold", 0.35f,
			"below this local scale, registry fast polls run every other frame");

		QueueCap = Config.Bind("Queue", "QueueCap", 500, "max_snapshot_queue cap for all coolsync subsystems (stock 2000)");
		JoinQueueCap = Config.Bind("Queue", "JoinQueueCap", 2000,
			"temp cap while any client is in join grace (0 = queuecap only)");
		JoinQueueCapGraceSeconds = Config.Bind("Queue", "JoinQueueCapGraceSeconds", 30f,
			"seconds after first coolsync per-player state to use joinqueuecap on host");
		QueueOverflowFraction = Config.Bind("Queue", "QueueOverflowFraction", 0.1f,
			"fraction of cap dropped on overflow (stock 0.5)");
		HeadPinTimeoutSeconds = Config.Bind("Queue", "HeadPinTimeoutSeconds", 10f,
			"seconds before re-anchor + force-dequeue pinned per-player snapshot_queue head");
		StaticPinTimeoutSeconds = Config.Bind("Queue", "StaticPinTimeoutSeconds", 10f,
			"seconds before force-dequeue pinned head on rulesyncer/worldstate server_snapshot_queue");
		ForceSyncHeadTimeoutSeconds = Config.Bind("Queue", "ForceSyncHeadTimeoutSeconds", 10f,
			"seconds before evicting stale head of per-player forcesync_queue");
		HeadPinMaxEvictPerPlayerPerTick = Config.Bind("Queue", "HeadPinMaxEvictPerPlayerPerTick", 2,
			"max snapshot pin-timeout evictions per client per frame");
		ForceSyncMaxEvictPerPlayerPerTick = Config.Bind("Queue", "ForceSyncMaxEvictPerPlayerPerTick", 4,
			"max forcesync queue evictions per client per frame");
		AckDrainMaxPerReceive = Config.Bind("Drain", "AckDrainMaxPerReceive", 8,
			"max snapshot clears per client ack (0 = off)");

		HotLaneCapPerTick = Config.Bind("Producer", "HotLaneCapPerTick", 24,
			"max hot-lane force-sync enqueues per client per frame");
		NearLaneCapPerTick = Config.Bind("Producer", "NearLaneCapPerTick", 12,
			"max near-lane force-sync enqueues per client per frame");
		DormantLaneCapPerTick = Config.Bind("Producer", "DormantLaneCapPerTick", 4,
			"max dormant-lane force-sync enqueues per client per frame");
		SafetyNetRadius = Config.Bind("Producer", "SafetyNetRadius", 32f,
			"radius (m) for low-rate safety-net gather near each player");
		SafetyNetHz = Config.Bind("Producer", "SafetyNetHz", 1f, "safety-net sweep frequency");
		SafetyNetMaxPerSweep = Config.Bind("Producer", "SafetyNetMaxPerSweep", 16,
			"max register+enqueue ops per player per safety-net sweep");
		CullDistance = Config.Bind("Producer", "CullDistance", 240f,
			"Euclidean fallback when CPUOptimization chunk simulation is inactive; otherwise use authority sim chunks");
		CullIntervalSeconds = Config.Bind("Producer", "CullIntervalSeconds", 8f,
			"registry cull interval in seconds");
		JoinBurstMax = Config.Bind("Producer", "JoinBurstMax", 200,
			"max objects to seed for joining client during join burst");
		AutoRegisterRadius = Config.Bind("Producer", "AutoRegisterRadius", 0f,
			"if > 0, limited auto-register near players (0 = event/safety-net only)");
		ContainerPrioritySyncEnabled = Config.Bind("Producer", "ContainerPrioritySyncEnabled", true,
			"register and hot-sync container contents when opened");

		GcSliceMs = Config.Bind("GC", "GcSliceMs", 1.5f,
			"unity incremental gc slice in ms (runtime only)");
		SkipUnchangedObjects = Config.Bind("Skip", "SkipUnchangedObjects", true,
			"skip packing unchanged objects on regular round-robin send");
		DebugLog = Config.Bind("Telemetry", "DebugLog", false, "emit window telemetry line every window");
		TelemetryWindowSeconds = Config.Bind("Telemetry", "TelemetryWindowSeconds", 10f, "rolling stats window");
		VerboseLogging = Config.Bind("Telemetry", "VerboseLogging", false, "first-hit event markers");
		FrameTimingEnabled = Config.Bind("Profiler", "FrameTimingEnabled", false,
			"measure wall-time per hot path (ms/frame)");
		FrameTimingCsvEnabled = Config.Bind("Profiler", "FrameTimingCsvEnabled", false,
			"append profiler windows to persistentdatapath/krokmpoptimization2/profiler.csv");

		SkipIdleSkinRebuild = Config.Bind("Memory", "SkipIdleSkinRebuild", true,
			"skip krokmp imgui skin rebuild when scale/resolution unchanged");
		DeduplicateInGameUi = Config.Bind("Memory", "DeduplicateInGameUi", true,
			"drop the second in-world uiingame pass each ongui");
		UiOptimizationExperimental = Config.Bind("UI", "UiOptimizationExperimental", false,
			"UI Optimization (experimental) - may shrink or break multiplayer menus and in-game UI. Set to false (default) to disable any feature that attempts to optimize UI calls, skin rebuilds, dedups, texture destroys, etc.");
		CompressPooling = Config.Bind("Memory", "CompressPooling", true,
			"reuse gzip/deflate scratch streams. still calls compresswriter so receivers can decompressreader");
		FixUnconsciousText = Config.Bind("Memory", "FixUnconsciousText", true,
			"replace consciousnesstext += \"!\" with a fixed string");
		PruneCoolSyncObjStates = Config.Bind("Memory", "PruneCoolSyncObjStates", true,
			"remove orphan coolsync objstates on cull / layer flush / transport end");
		PruneSteamAvatars = Config.Bind("Memory", "PruneSteamAvatars", true,
			"destroy unused steam avatar texture2ds on leave / transport end");
		CoolSyncAllocPooling = Config.Bind("Memory", "CoolSyncAllocPooling", true,
			"reuse coolsync object lists, skip linq tolist/elementat/any, pool snapshots and bitset buffers");
		CoolSyncWriterPool = Config.Bind("Memory", "CoolSyncWriterPool", true,
			"reuse one netdatawriter in coolsync packandsend. does not replace net.createwriter globally");
		HeapProbeSeconds = Config.Bind("Memory", "HeapProbeSeconds", 0f,
			"seconds between targeted heap probes (texture2d/material/tmp/steam/snaps). 0 = off");
		HeapProbeKey = Config.Bind("Memory", "HeapProbeKey", KeyCode.F9,
			"key that runs heap probe immediately. none disables hotkey");
		HeapProbeForceCollect = Config.Bind("Memory", "HeapProbeForceCollect", false,
			"if true, f9 also logs monoused before/after gc.collect. never on the 10s tick");
		ObjStatePruneTickSeconds = Config.Bind("Memory", "ObjStatePruneTickSeconds", 30f,
			"host-only orphan objstate prune interval. 0 = only on layer flush / transport end");
		DestroyUiTexturesOnResize = Config.Bind("Memory", "DestroyUiTexturesOnResize", true,
			"destroy cached ui texture2ds before resizeguitextures clears them");
		DestroyLimbMaterials = Config.Bind("Memory", "DestroyLimbMaterials", true,
			"destroy per-limb instance materials when limb destroyed");
		MemoryTelemetryEnabled = Config.Bind("Memory", "Telemetry", false,
			"first-hit + 10s memory counters. independent of host producer");

		ElderHiFiEnabled = Config.Bind("ElderHiFi", "Enabled", true,
			"high-frequency authoritative sideband sync for Elder Thornback (only to modded clients; falls back to object sync)");
		ElderHiFiRateHz = Config.Bind("ElderHiFi", "RateHz", 25,
			"send rate for elder snapshots (Hz)");
		ElderHiFiRadius = Config.Bind("ElderHiFi", "Radius", 80f,
			"only hi-fi when player within this distance");
		SkipIdleCatPatchControllers = Config.Bind("CatPatch", "SkipIdleControllers", true,
			"skip CatPatch emote/hug/kiss/dogpile/piggy/updater Update when that feature is idle");

		bool hostEnabled = Enabled.Value;
		bool clientReliefEnabled = ClientReliefEnabled.Value;
		bool uiOptEnabled = UiOptimizationExperimental != null && UiOptimizationExperimental.Value;
		bool memoryEnabled = MemoryTelemetryEnabled.Value
		                     || (uiOptEnabled && SkipIdleSkinRebuild.Value)
		                     || (uiOptEnabled && DeduplicateInGameUi.Value)
		                     || CompressPooling.Value
		                     || FixUnconsciousText.Value
		                     || PruneCoolSyncObjStates.Value
		                     || PruneSteamAvatars.Value
		                     || CoolSyncAllocPooling.Value
		                     || CoolSyncWriterPool.Value
		                     || HeapProbeSeconds.Value > 0f
		                     || (uiOptEnabled && DestroyUiTexturesOnResize.Value)
		                     || DestroyLimbMaterials.Value;

		if (!hostEnabled && !clientReliefEnabled && !memoryEnabled)
		{
			OptLog.Warn("[KrokMPOpt2] disabled via config - no patches applied.");
			return;
		}

		if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(ClientReliefBootstrap.StandaloneGuid))
		{
			OptLog.Error(
				"[KrokMPOpt2] KrokMPClientRelief is loaded - remove BepInEx/plugins/KrokMPClientRelief/KrokMPClientRelief.dll (built into v2.1+).");
			if (clientReliefEnabled)
			{
				OptLog.Warn("[KrokMPOpt2] skipping baked-in client relief to avoid duplicate patches.");
				clientReliefEnabled = false;
			}
		}

		try
		{
			_harmony = new Harmony(PluginInfo.GUID);

			if (clientReliefEnabled)
			{
				ClientReliefBootstrap.Register(_harmony);
				gameObject.AddComponent<LocalFpsScaleHost>();
			}

		if (memoryEnabled)
			MemoryBootstrap.Register(_harmony);

		if (Plugin.ElderHiFiEnabled != null && Plugin.ElderHiFiEnabled.Value)
			ElderHiFiSync.Register(_harmony);

		CatPatchReactivityPatches.Apply(_harmony);

		// Ghost-item guard runs on clients before the host-only early return.
		// It also remains harmless when the expected stock signatures are absent.
		try
		{
			if (ObjectCullPatch.TryApply(_harmony))
				Log.LogInfo("[KrokMPOpt2] Object culling patch applied");
			CullReconcileSweep.Ensure(gameObject);
		}
		catch (Exception ex)
		{
			Log.LogWarning($"[KrokMPOpt2] Failed to apply ObjectCullPatch: {ex.Message}");
		}

		// Always patch transport lifecycle so client-side announce and logging work even in client-only installs
		_harmony.PatchAll(typeof(TransportLifecyclePatch));
		_harmony.PatchAll(typeof(TransportEndPatch));

		if (!hostEnabled)
		{
			OptLog.Info(
				$"loaded v{PluginInfo.Version} - client/memory only (host producer disabled).");
			return;
		}

		GcSlice.Apply(GcSliceMs.Value);
		InventoryHelper.TryResolve();
		InventoryGatherFilter.TryResolve();

			if (!CoolSyncReflect.TryResolve())
			{
				OptLog.Error("[KrokMPOpt2] CoolSyncReflect failed - host patches skipped.");
				return;
			}

			OptLog.Info(
				$"[KrokMPOpt2] startup cap={QueueCap.Value} pinTimeout={HeadPinTimeoutSeconds.Value:F0}s " +
				$"forceSyncTimeout={ForceSyncHeadTimeoutSeconds.Value:F0}s gcSliceMs={GcSliceMs.Value:F1} " +
				$"lanes hot/near/dorm={HotLaneCapPerTick.Value}/{NearLaneCapPerTick.Value}/{DormantLaneCapPerTick.Value}");

			_harmony.PatchAll(typeof(DisableFastSyncPatch));
			_harmony.PatchAll(typeof(SuppressRegistryGatherPatch));
			_harmony.PatchAll(typeof(DeadObjectDistancePatch));
			_harmony.PatchAll(typeof(EventHookPatches));
			_harmony.PatchAll(typeof(EventHookForOnePatch));
			_harmony.PatchAll(typeof(EventHookObjectSyncSinglePatch));
			_harmony.PatchAll(typeof(ContainerPolicyPatches));
			_harmony.PatchAll(typeof(QueueCapPatch));
			_harmony.PatchAll(typeof(QueueOverflowPatch));
			_harmony.PatchAll(typeof(HeadPinTimeoutPatch));
			_harmony.PatchAll(typeof(StaticQueuePinTimeoutPatch));
			_harmony.PatchAll(typeof(StaticSnapshotEnqueuePatch));
			_harmony.PatchAll(typeof(ForceSyncQueueTimeoutPatch));
			_harmony.PatchAll(typeof(ForceSyncQueueEvictPatch));
			_harmony.PatchAll(typeof(LayerQueueFlushPatch));
			_harmony.PatchAll(typeof(SkipUnchangedPatch));
			_harmony.PatchAll(typeof(AckDrainPatch));
			_harmony.PatchAll(typeof(PlayerJoinGracePatch));
			ContainerPolicyPatches.RegisterNetHandler();

			QueueCapPatch.ApplyToAll("Awake");

			_producerHost = gameObject.AddComponent<SyncProducerHost>();
			_hostArmed = true;

			if (ElderHiFiEnabled != null && ElderHiFiEnabled.Value)
				ElderHiFiSync.EnsureTickHost(gameObject);

			OptLog.Info(
				$"[KrokMPOpt2] loaded v{PluginInfo.Version} - host producer armed; activates on MP transport start.");
		}
		catch (Exception ex)
		{
			OptLog.Error("Awake", ex);
		}
	}

	private void Update()
	{
		_probeAge += Time.unscaledDeltaTime;
		if (_probeAge >= 10f)
		{
			_probeAge = 0f;
			LogFeatureProbes();
		}

		if (!_hostArmed || Plugin.Enabled == null || !Plugin.Enabled.Value)
			return;

		if (IsHostSessionActive)
		{
			JoinQueueGrace.Tick();
		}

		if (Time.frameCount == 120)
		{
			GcSlice.Apply(GcSliceMs.Value);
			if (IsHostSessionActive)
				QueueCapPatch.ApplyToAll("frame120");
		}
	}

	private static void LogFeatureProbes()
	{
		if (UiOptimizationExperimental != null && UiOptimizationExperimental.Value)
		{
			ImguiSkinSkipPatch.ConsumeProbe(out int stamp, out int skip, out int open, out int scale, out int first);
			OptLog.Info(
				$"[KrokMPOpt2] imgui-skin stamp={stamp} skip={skip} reasonOpen={open} reasonScale={scale} reasonFirst={first}");
		}

		if (CatPatchReactivityPatches.Hooked)
		{
			CatPatchReactivityPatches.ConsumeProbe(
				out int se, out int sh, out int sk, out int sd,
				out int sp, out int su, out int run, out int fot,
				out int sog, out int sov);
			OptLog.Info(
				$"[KrokMPOpt2] catpatch hooked=1 skipEmote={se} skipHug={sh} skipKiss={sk} " +
				$"skipDogpile={sd} skipPiggy={sp} skipUpdater={su} runSocial={run} fotBody={fot} " +
				$"skipOnGui={sog} skipOverlay={sov}");
		}
	}

	private void OnDestroy()
	{
		try
		{
			SteamAvatarPrune.Detach();
			_harmony?.UnpatchSelf();
		}
		catch (Exception ex)
		{
			OptLog.Error("OnDestroy", ex);
		}
	}
}

internal static class PluginInfo
{
	public const string GUID = "com.local.krokmp.optimization2";
	public const string Name = "KrokMPOptimization2";
	public const string Version = "2.2.13";
}
