using KrokoshaCasualtiesMP;
using UnityEngine;

namespace KrokMPOptimization2;

// runs the host producer once per Unity frame while an MP session is active.
internal sealed class SyncProducerHost : MonoBehaviour
{
	private void Update()
	{
		if (Plugin.Enabled == null || !Plugin.Enabled.Value || !Plugin.IsHostSessionActive)
			return;

		double t0 = FrameTiming.Begin();
		JoinBurst.Tick();
		SafetyNetSweep.Tick(Time.unscaledDeltaTime);
		int flushed = LaneScheduler.FlushAll();
		if (flushed > 0)
			ModTelemetry.AddLaneFlush(flushed);
		FrameTiming.End("syncProducer", t0);
	}
}
