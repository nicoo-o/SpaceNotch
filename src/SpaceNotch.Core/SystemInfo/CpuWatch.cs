namespace SpaceNotch.Core.SystemInfo;

/// <summary>Ce que le moniteur décide après une mesure.</summary>
public enum CpuVerdict
{
    /// <summary>Rien à dire.</summary>
    None,

    /// <summary>Le processeur est saturé depuis assez longtemps : la pastille apparaît.</summary>
    Alert,

    /// <summary>L'alerte est en cours : la courbe se met à jour.</summary>
    Ongoing,

    /// <summary>La charge est retombée : la pastille part.</summary>
    Clear
}

/// <summary>
/// Moniteur système (F6) : rien au repos. Si le processeur reste au-dessus de
/// <see cref="Threshold"/> pendant <see cref="Sustain"/>, une alerte s'ouvre ;
/// elle se referme quand la charge redescend sous <see cref="Release"/>
/// pendant <see cref="Calm"/>. L'écart entre les deux seuils évite qu'une
/// charge qui oscille autour de 85 % fasse clignoter la notch.
/// </summary>
public sealed class CpuWatch
{
    public const double Threshold = 85;
    public const double Release = 70;
    public static readonly TimeSpan Sustain = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan Calm = TimeSpan.FromSeconds(8);

    /// <summary>Points de la mini-courbe.</summary>
    public const int HistoryLength = 30;

    private readonly Queue<double> _history = new();
    private DateTimeOffset? _hotSince;
    private DateTimeOffset? _coolSince;

    /// <summary>Vrai tant que l'alerte est ouverte.</summary>
    public bool IsAlerting { get; private set; }

    /// <summary>Les dernières mesures, de la plus ancienne à la plus récente (0..100).</summary>
    public IReadOnlyList<double> History => [.. _history];

    /// <summary>Nouvelle mesure du processeur, en pourcentage.</summary>
    public CpuVerdict Add(double percent, DateTimeOffset now)
    {
        percent = Math.Clamp(percent, 0, 100);
        _history.Enqueue(percent);

        while (_history.Count > HistoryLength)
        {
            _history.Dequeue();
        }

        if (!IsAlerting)
        {
            _hotSince = percent >= Threshold ? _hotSince ?? now : null;

            if (_hotSince is { } since && now - since >= Sustain)
            {
                IsAlerting = true;
                _coolSince = null;
                return CpuVerdict.Alert;
            }

            return CpuVerdict.None;
        }

        _coolSince = percent < Release ? _coolSince ?? now : null;

        if (_coolSince is { } cool && now - cool >= Calm)
        {
            IsAlerting = false;
            _hotSince = null;
            _coolSince = null;
            return CpuVerdict.Clear;
        }

        return CpuVerdict.Ongoing;
    }

    /// <summary>
    /// Fraction de temps processeur entre deux relevés de
    /// <c>GetSystemTimes</c> : (noyau + utilisateur − inactif) / (noyau + utilisateur).
    /// Le temps noyau inclut le temps inactif, d'où la soustraction.
    /// </summary>
    public static double Percent(long idleDelta, long kernelDelta, long userDelta)
    {
        long total = kernelDelta + userDelta;
        return total <= 0 ? 0 : Math.Clamp(100.0 * (total - idleDelta) / total, 0, 100);
    }

    /// <summary>
    /// Processus que la notch ne propose jamais de fermer : ceux du système,
    /// et SpaceNotch elle-même.
    /// </summary>
    public static bool IsProtected(string name, int processId, int ownId)
        => processId <= 4
            || processId == ownId
            || Protected.Contains(name);

    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass",
        "svchost", "dwm", "explorer", "fontdrvhost", "Memory Compression", "MsMpEng", "SpaceNotch"
    };
}
