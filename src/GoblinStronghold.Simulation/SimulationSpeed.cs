namespace GoblinStronghold.Simulation;

public enum SimulationSpeed
{
    Paused = 0,
    Normal = 1,
    Double = 2,
    Quadruple = 4,
    Octuple = 8,
    Unthrottled = -1,
}

public sealed class SimulationRunner
{
    public SimulationRunner(SimulationEngine engine)
    {
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public SimulationEngine Engine { get; }

    public int RunBatch(SimulationSpeed speed, int unthrottledTickBudget = 256)
    {
        var tickCount = ResolveTickCount(speed, unthrottledTickBudget);
        Engine.AdvanceTicks(tickCount);
        return tickCount;
    }

    public void RunUntil(
        SimulationTick targetTick,
        SimulationSpeed speed,
        int unthrottledTickBudget = 256,
        Action<SimulationSnapshot>? snapshotConsumer = null)
    {
        if (targetTick.Value < Engine.CurrentTick.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(targetTick));
        }

        var ticksPerBatch = ResolveTickCount(speed, unthrottledTickBudget);
        if (ticksPerBatch == 0 && targetTick != Engine.CurrentTick)
        {
            throw new InvalidOperationException("Paused simulation cannot advance to a future tick.");
        }

        while (Engine.CurrentTick.Value < targetTick.Value)
        {
            var remaining = targetTick.Value - Engine.CurrentTick.Value;
            var currentBatch = checked((int)Math.Min(remaining, ticksPerBatch));
            Engine.AdvanceTicks(currentBatch);
            snapshotConsumer?.Invoke(Engine.CreateSnapshot());
        }
    }

    private static int ResolveTickCount(SimulationSpeed speed, int unthrottledTickBudget)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unthrottledTickBudget);

        return speed switch
        {
            SimulationSpeed.Paused => 0,
            SimulationSpeed.Normal => 1,
            SimulationSpeed.Double => 2,
            SimulationSpeed.Quadruple => 4,
            SimulationSpeed.Octuple => 8,
            SimulationSpeed.Unthrottled => unthrottledTickBudget,
            _ => throw new ArgumentOutOfRangeException(nameof(speed)),
        };
    }
}
