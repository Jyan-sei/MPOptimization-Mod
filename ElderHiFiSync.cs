using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace KrokMPOptimization2;

/// <summary>
/// Capability gated high freq sideband for Elder Thornback (perfect sync when both sides modded).
/// Host runs full AI and decides "move to" targets.
/// Clients ingest host's target coords into local sim (SpiderHandler.target + moveTime held high to disable local "move to point" AI choice).
/// This makes client local sim (FixedUpdate forces, legs IK, etc) follow the exact same high-level commands as host with minimal delay.
/// Host always keeps normal object sync for fallback / vanilla clients.
/// Only sends extra snapshots to clients that announced support.
/// </summary>
internal static class ElderHiFiSync
{
	private static readonly Dictionary<knetid, bool> HiFiClients = new Dictionary<knetid, bool>();
	private static bool _receiversDone;
	private static bool _announced;
	private static float _lastAnnounceLogTime;
	private static int _sentCount;
	private static float _announceAfter;
	private static float _nextAnnounceTime;
	private static bool _worldgenDone;
	private static bool _serverReceiversDone;
	private static readonly List<ElderThornbackBehaviour> CachedElders = new List<ElderThornbackBehaviour>(8);
	private static readonly Dictionary<knetid, List<ElderThornbackBehaviour>> InRangeByPlayer =
		new Dictionary<knetid, List<ElderThornbackBehaviour>>(8);
	private static bool _eldersScanned;
	private static float _probeAge;
	private static int _probeSends;
	private static int _probeInRangePairs;
	private static bool _loggedRangeEnter;
	private static bool _sendFieldsReady;
	private static FieldInfo _stageField;
	private static FieldInfo _meField;
	private static FieldInfo _targetField;
	private static FieldInfo _biteField;
	private static FieldInfo _stunField;
	private static FieldInfo _buildField;
	private static FieldInfo _healthField;
	private static FieldInfo _lookForceField;
	private static FieldInfo _moveForceField;

	private const ushort MsgHandshake = 10500; // client -> server announce
	private const ushort MsgSnapshot = 10501;  // server -> client snapshot

	internal static void Register(Harmony harmony)
	{
		if (!Plugin.ElderHiFiEnabled.Value) return;

		try
		{
			// Patch to register receivers after net init
			var clientType = typeof(ClientMain);
			var serverType = typeof(ServerMain);

			var clientAwake = AccessTools.Method(clientType, "Awake") ?? AccessTools.Method(clientType, "Start");
			if (clientAwake != null)
				harmony.Patch(clientAwake, postfix: new HarmonyMethod(typeof(ElderHiFiSync), nameof(ClientPostfix)));

			var clientUpdate = AccessTools.Method(clientType, "Update");
			if (clientUpdate != null)
				harmony.Patch(clientUpdate, postfix: new HarmonyMethod(typeof(ElderHiFiSync), nameof(ClientUpdatePostfix)));

			var serverAwake = AccessTools.Method(serverType, "Awake") ?? AccessTools.Method(serverType, "Start");
			if (serverAwake != null)
				harmony.Patch(serverAwake, postfix: new HarmonyMethod(typeof(ElderHiFiSync), nameof(ServerPostfix)));

			// Register server handlers at the same time as core attribute-based receivers
			var serverReg = AccessTools.Method(typeof(ServerMain), "_RegisterServerReceivers");
			if (serverReg != null)
				harmony.Patch(serverReg, postfix: new HarmonyMethod(typeof(ElderHiFiSync), nameof(ServerReceiversPostfix)));

			// Register client handlers at the same time as core attribute-based receivers (proper timing, after transport)
			var clientReg = AccessTools.Method(typeof(ClientMain), "_RegisterClientReceivers");
			if (clientReg != null)
				harmony.Patch(clientReg, postfix: new HarmonyMethod(typeof(ElderHiFiSync), nameof(ClientReceiversPostfix)));

			OptLog.Info("[ElderHiFi] patched ClientMain/ServerMain (Awake/Start + client Update for announce + _Register*Receivers).");

			if (Plugin.ElderHiFiEnabled != null && Plugin.ElderHiFiEnabled.Value)
			{
				PatchInvokeForDebug(harmony);

				var elderStart = AccessTools.Method(typeof(ElderThornbackBehaviour), "Start");
				if (elderStart != null)
					harmony.Patch(elderStart, postfix: new HarmonyMethod(typeof(ElderHiFiSync), nameof(ElderStartPostfix)));

				var elderDestroy = AccessTools.Method(typeof(ElderThornbackBehaviour), "OnDestroy");
				if (elderDestroy != null)
					harmony.Patch(elderDestroy, postfix: new HarmonyMethod(typeof(ElderHiFiSync), nameof(ElderDestroyPostfix)));
			}

			SubscribeWorldGenFinish();
		}
		catch (Exception ex)
		{
			OptLog.Warn($"[ElderHiFi] register failed: {ex.Message}");
		}
	}

	static void ClientPostfix()
	{
		OptLog.Info("[ElderHiFi] ClientPostfix fired (on ClientMain Awake/Start)");
	}

	static void ClientUpdatePostfix()
	{
		if (_worldgenDone && !_announced && Plugin.ElderHiFiEnabled != null && Plugin.ElderHiFiEnabled.Value)
		{
			AnnounceSupport();
		}
	}

	static void ServerPostfix()
	{
		// legacy; main server reg now in ServerReceiversPostfix
	}

	static void ServerReceiversPostfix()
	{
		RegisterServerHandlers();
	}

	static void ClientReceiversPostfix()
	{
		RegisterClientHandlers();
	}

	static void SubscribeWorldGenFinish()
	{
		try
		{
			var t = AccessTools.TypeByName("KrokoshaCasualtiesMP.WorldgenPatches");
			if (t == null) return;
			var evt = t.GetEvent("OnWorldgenFinish");
			if (evt == null) return;
			var handlerType = evt.EventHandlerType;
			var method = typeof(ElderHiFiSync).GetMethod(nameof(OnWorldgenFinish), BindingFlags.Static | BindingFlags.NonPublic);
			var del = Delegate.CreateDelegate(handlerType, method);
			evt.AddEventHandler(null, del);
			OptLog.Info("[ElderHiFi] subscribed to OnWorldgenFinish");
		}
		catch (Exception ex)
		{
			OptLog.Warn($"[ElderHiFi] subscribe to worldgen finish failed: {ex.Message}");
		}
	}

	static void OnWorldgenFinish()
	{
		if (Net.is_server)
		{
			ScanEldersAfterWorldgen();
			return;
		}

		if (Plugin.ElderHiFiEnabled != null && Plugin.ElderHiFiEnabled.Value)
		{
			_announced = false;
			_sentCount = 0;
			_announceAfter = Time.realtimeSinceStartup - 10f;
			_nextAnnounceTime = 0f;
			_worldgenDone = true;
			AnnounceSupport();
		}
	}

	internal static void ScanEldersAfterWorldgen()
	{
		CachedElders.Clear();
		ElderThornbackBehaviour[] found = UnityEngine.Object.FindObjectsOfType<ElderThornbackBehaviour>(true);
		for (int i = 0; i < found.Length; i++)
		{
			if (found[i] != null)
				CachedElders.Add(found[i]);
		}
		_eldersScanned = true;
		OptLog.Info($"[ElderHiFi] worldgen scan cached elders={CachedElders.Count}");
	}

	internal static void ClearElderCache()
	{
		CachedElders.Clear();
		InRangeByPlayer.Clear();
		_eldersScanned = false;
	}

	internal static List<ElderThornbackBehaviour> GetCachedElders() => CachedElders;

	internal static bool EldersScanned => _eldersScanned;

	internal static void EnsureTickHost(GameObject go = null)
	{
		if (!Net.is_server) return;
		if (go == null)
		{
			go = new GameObject("ElderHiFiTickHost");
			UnityEngine.Object.DontDestroyOnLoad(go);
		}
		if (go.GetComponent<ElderHiFiTickHost>() == null)
			go.AddComponent<ElderHiFiTickHost>();
	}

	static void RegisterClientHandlers()
	{
		if (_receiversDone) return;
		try
		{
			var reg = typeof(Net).GetMethod("RegisterClientReceiver", BindingFlags.Static | BindingFlags.NonPublic);
			if (reg == null)
			{
				OptLog.Warn("[ElderHiFi] RegisterClientReceiver method not found on Net - cannot receive snapshots");
				return;
			}
			var paramType = reg.GetParameters()[1].ParameterType;
			var del = Delegate.CreateDelegate(paramType, typeof(ElderHiFiSync).GetMethod(nameof(OnSnapshot), BindingFlags.Static | BindingFlags.NonPublic));
			reg.Invoke(null, new object[] { MsgSnapshot, del });
			_receiversDone = true;
			OptLog.Info("[ElderHiFi] client handlers registered (for snapshot 10501).");
		}
		catch (Exception ex)
		{
			OptLog.Warn($"[ElderHiFi] client reg failed: {ex.Message}");
		}
	}

	static void RegisterServerHandlers()
	{
		if (_serverReceiversDone) return;
		try
		{
			var reg = typeof(Net).GetMethod("RegisterServerReceiver", BindingFlags.Static | BindingFlags.NonPublic);
			if (reg == null)
			{
				OptLog.Warn("[ElderHiFi] RegisterServerReceiver method not found on Net - cannot receive handshakes");
				return;
			}
			var handler = typeof(ElderHiFiSync).GetMethod(nameof(OnHandshake), BindingFlags.Static | BindingFlags.NonPublic);
			var del = (KrokoshaScavMultiplayer.KrokoshaHandleNamedMessageDelegate)
				Delegate.CreateDelegate(typeof(KrokoshaScavMultiplayer.KrokoshaHandleNamedMessageDelegate), handler);
			reg.Invoke(null, new object[] { MsgHandshake, del });
			_serverReceiversDone = true;
			OptLog.Info("[ElderHiFi] server handlers registered (for handshake 10500).");
		}
		catch (Exception ex)
		{
			OptLog.Warn($"[ElderHiFi] server reg failed: {ex.Message}");
		}
	}

	static void PatchInvokeForDebug(Harmony harmony)
	{
		try
		{
			var invoke = AccessTools.Method(typeof(Net), "InvokeServerMessage");
			if (invoke != null)
			{
				harmony.Patch(invoke, prefix: new HarmonyMethod(typeof(ElderHiFiSync), nameof(InvokeServerMessagePrefix)));
				OptLog.Info("[ElderHiFi] patched InvokeServerMessage for 10500 debug peek.");
			}
		}
		catch (Exception ex)
		{
			OptLog.Warn($"[ElderHiFi] debug invoke patch failed: {ex.Message}");
		}
	}

	static void InvokeServerMessagePrefix(knetid callerclientId, NetDataReader reader)
	{
		if (reader == null) return;
		try
		{
			if (reader.RawData != null && reader.Position + 2 <= reader.RawData.Length)
			{
				ushort id = BitConverter.ToUInt16(reader.RawData, reader.Position);
				if (id == MsgHandshake)
				{
					OptLog.Info($"[ElderHiFi DEBUG] InvokeServerMessage saw 10500 from callerclientId={callerclientId}");
				}
			}
		}
		catch
		{
			// best effort debug only
		}
	}

	static void ElderStartPostfix(ElderThornbackBehaviour __instance)
	{
		if (!Net.is_server || Plugin.ElderHiFiEnabled == null || !Plugin.ElderHiFiEnabled.Value)
			return;
		if (__instance == null)
			return;
		if (!CachedElders.Contains(__instance))
			CachedElders.Add(__instance);
		OptLog.Info($"[ElderHiFi] elder started cache={CachedElders.Count}");
	}

	static void ElderDestroyPostfix(ElderThornbackBehaviour __instance)
	{
		if (__instance == null)
			return;
		CachedElders.Remove(__instance);
	}

	static void OnHandshake(knetid sender, ref NetDataReader reader)
	{
		HiFiClients[sender] = true;
		OptLog.Info($"[ElderHiFi] client {sender} supports hi-fi");
	}

	internal static void AnnounceSupport()
	{
		if (Plugin.ElderHiFiEnabled == null || !Plugin.ElderHiFiEnabled.Value) return;
		if (_announced) return;

		bool isSrv = Net.is_server;
		bool running = Net.running;
		bool connected = Net.is_connected;

		if (isSrv || !running || !connected)
		{
			if (Time.realtimeSinceStartup - _lastAnnounceLogTime > 2f)
			{
				OptLog.Info($"[ElderHiFi] AnnounceSupport: is_server={isSrv}, running={running}, connected={connected}, _receiversDone={_receiversDone} - skipped (will retry)");
				_lastAnnounceLogTime = Time.realtimeSinceStartup;
			}
			return;
		}

		if (_announceAfter == 0f)
		{
			_announceAfter = Time.realtimeSinceStartup + 3f; // delay to avoid early NRE / before host ready
		}
		if (Time.realtimeSinceStartup < _announceAfter)
		{
			return;
		}

		if (Time.realtimeSinceStartup < _nextAnnounceTime)
		{
			return;
		}

		if (Net.TRANSPORT == null)
		{
			OptLog.Warn("[ElderHiFi] TRANSPORT null, skipping announce send");
			return;
		}

		// log when attempting
		OptLog.Info($"[ElderHiFi] AnnounceSupport: is_server={isSrv}, running={running}, connected={connected} - attempting send (attempt {_sentCount + 1})");

		try
		{
			var w = Net.CreateWriter(MsgHandshake);
			w.Put((byte)1);
			Net.Client_Send(DeliveryMethod.ReliableUnordered, in w);
			_sentCount++;
			OptLog.Info($"[ElderHiFi] support announced to host (sent msg 10500 ReliableUnordered) attempt={_sentCount}");
			_lastAnnounceLogTime = Time.realtimeSinceStartup;
			_nextAnnounceTime = Time.realtimeSinceStartup + 2f;
			if (_sentCount >= 8)
			{
				_announced = true;
			}
		}
		catch (Exception ex)
		{
			OptLog.Error($"[ElderHiFi] announce send failed: {ex}");
		}
	}

	static void OnSnapshot(knetid sender, ref NetDataReader reader)
	{
		try
		{
			float px = reader.GetFloat();
			float py = reader.GetFloat();
			float rotZ = reader.GetFloat();
			float vx = reader.GetFloat();
			float vy = reader.GetFloat();
			float av = reader.GetFloat();
			int stage = reader.GetInt();
			float tx = reader.GetFloat();
			float ty = reader.GetFloat();
			float bite = reader.GetFloat();
			float stun = reader.GetFloat();
			float health = reader.GetFloat();
			float lookForceMult = reader.GetFloat();
			float moveForce = reader.GetFloat();

			// Apply to nearby elders
			var elders = UnityEngine.Object.FindObjectsOfType<ElderThornbackBehaviour>(true);
			foreach (var elder in elders)
			{
				if (elder == null) continue;
				var go = elder.gameObject;
				if (go == null) continue;
				if (Vector2.Distance(go.transform.position, new Vector2(px, py)) > 200f) continue;

				var rb = go.GetComponent<Rigidbody2D>();

				// Always ingest current rotation and ang vel so the local SignedAngle/torque calc in FixedUpdate
				// starts from identical state as host (prevents accumulative rot desync).
				// Only force pos + linear vel on large drift to avoid jitter.
				go.transform.rotation = Quaternion.Euler(0, 0, rotZ);
				if (rb != null)
				{
					rb.angularVelocity = av;
				}

				float dist = Vector2.Distance(go.transform.position, new Vector2(px, py));
				if (dist > 5f)
				{
					go.transform.position = new Vector3(px, py, go.transform.position.z);
					if (rb != null)
					{
						rb.velocity = new Vector2(vx, vy);
					}
				}

				var stageF = typeof(ElderThornbackBehaviour).GetField("stage", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				stageF?.SetValue(elder, stage);
				var meF = typeof(ElderThornbackBehaviour).GetField("me", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				var spider = meF?.GetValue(elder) as SpiderHandler;
				var buildF = typeof(ElderThornbackBehaviour).GetField("build", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				var build = buildF?.GetValue(elder) as BuildingEntity;

				if (spider != null)
				{
					var targetF = typeof(SpiderHandler).GetField("target", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
					targetF?.SetValue(spider, new Vector2(tx, ty));

					// Key for local sim match: disable client's autonomous "move to point" AI
					// (the random target or threat target choice in SpiderHandler.Update).
					// Host's target (the "Elder moving here" coord) is ingested here.
					// Holding moveTime high prevents the moveTime<=0 block from overwriting target.
					// Local FixedUpdate will then drive forces/torque/legs toward the host-chosen target.
					var moveTimeF = typeof(SpiderHandler).GetField("moveTime", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
					moveTimeF?.SetValue(spider, 10f);

					var biteF = typeof(SpiderHandler).GetField("biteCooldown", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
					biteF?.SetValue(spider, bite);
					var stunF = typeof(SpiderHandler).GetField("stunTime", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
					stunF?.SetValue(spider, stun);

					// Sync turn rate / force multipliers + health for identical torque/force/lerp calcs (1:1 with host)
					var lfmF = typeof(SpiderHandler).GetField("lookForceMult", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
					lfmF?.SetValue(spider, lookForceMult);
					var mfF = typeof(SpiderHandler).GetField("moveForce", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
					mfF?.SetValue(spider, moveForce);

					if (build != null)
					{
						var hF = typeof(BuildingEntity).GetField("health", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
						hF?.SetValue(build, health);
					}

					OptLog.Info($"[ElderHiFi] client ingested target=({tx:F1},{ty:F1}) stage={stage} bite={bite:F2} stun={stun:F2} (pos drift was {dist:F1})");
				}
			}
		}
		catch (Exception ex)
		{
			OptLog.Warn($"[ElderHiFi] snapshot apply failed: {ex.Message}");
		}
	}

	internal static bool WantsHiFi(knetid id) => HiFiClients.TryGetValue(id, out var v) && v;

	internal static void Clear(knetid id)
	{
		HiFiClients.Remove(id);
		InRangeByPlayer.Remove(id);
	}

	internal static void CompactCachedElders()
	{
		for (int i = CachedElders.Count - 1; i >= 0; i--)
		{
			if (CachedElders[i] == null)
				CachedElders.RemoveAt(i);
		}
	}

	internal static void RefreshRangeGate()
	{
		CompactCachedElders();
		foreach (var kv in InRangeByPlayer)
			kv.Value.Clear();
		_probeInRangePairs = 0;
		if (CachedElders.Count == 0)
			return;

		float r = Plugin.ElderHiFiRadius != null ? Plugin.ElderHiFiRadius.Value : 80f;
		float r2 = r * r;
		foreach (var plr in NetPlayer.AllLivingPlayers)
		{
			if (plr == null || !WantsHiFi(plr.clientId))
				continue;
			if (!InRangeByPlayer.TryGetValue(plr.clientId, out List<ElderThornbackBehaviour> list))
			{
				list = new List<ElderThornbackBehaviour>(4);
				InRangeByPlayer[plr.clientId] = list;
			}

			Vector2 ppos = plr.pos;
			for (int i = 0; i < CachedElders.Count; i++)
			{
				ElderThornbackBehaviour e = CachedElders[i];
				if (e == null)
					continue;
				Vector3 ep = e.transform.position;
				float dx = ppos.x - ep.x;
				float dy = ppos.y - ep.y;
				if (dx * dx + dy * dy <= r2)
				{
					list.Add(e);
					_probeInRangePairs++;
				}
			}

			if (!_loggedRangeEnter && list.Count > 0)
			{
				_loggedRangeEnter = true;
				OptLog.Info($"[ElderHiFi] range enter player={plr.clientId} elders={list.Count}");
			}
		}
	}

	internal static void SendInRangeSnapshots()
	{
		if (CachedElders.Count == 0)
			return;
		foreach (var plr in NetPlayer.AllLivingPlayers)
		{
			if (plr == null || !WantsHiFi(plr.clientId))
				continue;
			if (!InRangeByPlayer.TryGetValue(plr.clientId, out List<ElderThornbackBehaviour> list) || list.Count == 0)
				continue;
			for (int i = 0; i < list.Count; i++)
			{
				ElderThornbackBehaviour e = list[i];
				if (e == null)
					continue;
				SendIfWanted(plr, e);
			}
		}
	}

	internal static void TickProbe(float dt)
	{
		_probeAge += dt;
		if (_probeAge < 10f)
			return;
		_probeAge = 0f;
		int hifi = 0;
		foreach (var kv in HiFiClients)
		{
			if (kv.Value)
				hifi++;
		}
		OptLog.Info(
			$"[ElderHiFi] scanned={(_eldersScanned ? 1 : 0)} elders={CachedElders.Count} " +
			$"hifiClients={hifi} inRangePairs={_probeInRangePairs} sends={_probeSends}");
		_probeSends = 0;
	}

	static void EnsureSendFields()
	{
		if (_sendFieldsReady)
			return;
		const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
		_stageField = typeof(ElderThornbackBehaviour).GetField("stage", f);
		_meField = typeof(ElderThornbackBehaviour).GetField("me", f);
		_targetField = typeof(SpiderHandler).GetField("target", f);
		_biteField = typeof(SpiderHandler).GetField("biteCooldown", f);
		_stunField = typeof(SpiderHandler).GetField("stunTime", f);
		_buildField = typeof(ElderThornbackBehaviour).GetField("build", f);
		_healthField = typeof(BuildingEntity).GetField("health", f);
		_lookForceField = typeof(SpiderHandler).GetField("lookForceMult", f);
		_moveForceField = typeof(SpiderHandler).GetField("moveForce", f);
		_sendFieldsReady = true;
	}

	internal static void SendIfWanted(NetPlayer plr, ElderThornbackBehaviour elder)
	{
		if (plr == null || elder == null || !Net.is_server) return;
		if (!WantsHiFi(plr.clientId)) return;

		try
		{
			EnsureSendFields();
			var go = elder.gameObject;
			var w = Net.CreateWriter(MsgSnapshot);

			w.Put(go.transform.position.x);
			w.Put(go.transform.position.y);
			w.Put(go.transform.eulerAngles.z);

			var rb = go.GetComponent<Rigidbody2D>();
			if (rb != null)
			{
				w.Put(rb.velocity.x);
				w.Put(rb.velocity.y);
				w.Put(rb.angularVelocity);
			}
			else
			{
				w.Put(0f); w.Put(0f); w.Put(0f);
			}

			w.Put(_stageField != null ? (int)_stageField.GetValue(elder) : 0);
			var spider = _meField?.GetValue(elder) as SpiderHandler;
			if (spider != null)
			{
				var t = _targetField != null ? (Vector2)_targetField.GetValue(spider) : Vector2.zero;
				w.Put(t.x);
				w.Put(t.y);
				w.Put(_biteField != null ? (float)_biteField.GetValue(spider) : 0f);
				w.Put(_stunField != null ? (float)_stunField.GetValue(spider) : 0f);

				var build = _buildField?.GetValue(elder) as BuildingEntity;
				w.Put(build != null && _healthField != null ? (float)_healthField.GetValue(build) : 0f);
				w.Put(_lookForceField != null ? (float)_lookForceField.GetValue(spider) : 1f);
				w.Put(_moveForceField != null ? (float)_moveForceField.GetValue(spider) : 0f);
			}
			else
			{
				w.Put(0f); w.Put(0f); w.Put(0f); w.Put(0f); w.Put(0f); w.Put(0f); w.Put(0f);
			}

			Net.Server_SendTo(DeliveryMethod.Unreliable, in w, plr);
			_probeSends++;
		}
		catch (Exception ex)
		{
			OptLog.Warn($"[ElderHiFi] send failed: {ex.Message}");
		}
	}
}

internal class ElderHiFiTickHost : MonoBehaviour
{
	private float _t;
	private float _rangeT;

	void Update()
	{
		if (!Net.is_server || Plugin.ElderHiFiEnabled == null || !Plugin.ElderHiFiEnabled.Value)
			return;
		ElderHiFiSync.TickProbe(Time.unscaledDeltaTime);
	}

	void FixedUpdate()
	{
		if (!Net.is_server || !Plugin.ElderHiFiEnabled.Value) return;
		if (!ElderHiFiSync.EldersScanned) return;

		_rangeT += Time.fixedDeltaTime;
		if (_rangeT >= 1f)
		{
			_rangeT = 0f;
			try
			{
				ElderHiFiSync.RefreshRangeGate();
			}
			catch (Exception ex)
			{
				OptLog.Warn($"[ElderHiFi] range gate failed: {ex.Message}");
			}
		}

		_t += Time.fixedDeltaTime;
		float iv = 1f / Mathf.Max(5, Plugin.ElderHiFiRateHz.Value);
		if (_t < iv) return;
		_t = 0;

		try
		{
			ElderHiFiSync.SendInRangeSnapshots();
		}
		catch (Exception ex)
		{
			OptLog.Warn($"[ElderHiFi] tick send loop failed: {ex.Message}");
		}
	}
}
