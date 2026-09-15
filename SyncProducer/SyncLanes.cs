namespace KrokMPOptimization2;

// names the producer lanes used to prioritize sync work per client.
internal enum SyncLane
{
	Hot = 0,
	Near = 1,
	Dormant = 2
}

internal enum SyncReason
{
	Event,
	Moved,
	SafetyNet,
	JoinBurst,
	ContainerOpen
}
