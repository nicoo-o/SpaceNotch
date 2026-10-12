using SpaceNotch.Core.SystemInfo;

namespace SpaceNotch.Platform.Windows.Power;

/// <summary>
/// La charge du processeur, mesurée une fois pour tout SpaceNotch (n° 74) : le
/// moniteur la mesure, Pixel reprend sa mesure si elle est assez récente.
/// </summary>
public static class CpuLoad
{
    private static readonly CpuSampler Sampler = new();

    /// <summary>Charge en pourcentage ; voir <see cref="SharedReading.Get"/>.</summary>
    public static SharedReading Shared { get; } = new(Sampler.Sample);
}
