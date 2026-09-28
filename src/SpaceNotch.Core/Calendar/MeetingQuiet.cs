namespace SpaceNotch.Core.Calendar;

/// <summary>
/// Silence de réunion (W2) : cinq minutes avant un rendez-vous, la pastille
/// propose de couper les notifications jusqu'à sa fin. Accepté, le silence
/// dure jusqu'à la fin prévue — ou une heure au plus quand l'événement n'en a
/// pas — et la notch dit ensuite combien de messages ont été retenus.
/// </summary>
public static class MeetingQuiet
{
    /// <summary>Durée du silence quand la réunion n'a pas de fin utilisable.</summary>
    public static readonly TimeSpan Fallback = TimeSpan.FromHours(1);

    /// <summary>Silence le plus long accepté : une réunion de toute une journée ne coupe pas tout.</summary>
    public static readonly TimeSpan Longest = TimeSpan.FromHours(4);

    /// <summary>La proposition a-t-elle un sens maintenant ?</summary>
    public static bool Offer(DateTimeOffset start, DateTimeOffset end, DateTimeOffset now, bool alreadyQuiet)
        => !alreadyQuiet && MeetingCountdown.Phase(start, end, now) != MeetingPhase.None;

    /// <summary>Jusqu'à quand couper.</summary>
    public static DateTimeOffset Until(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            return start + Fallback;
        }

        return end - start > Longest ? start + Longest : end;
    }

    /// <summary>« Silence jusqu'à 11:00 ».</summary>
    public static string Label(DateTimeOffset until, bool french)
        => french ? $"Silence jusqu'à {until.ToLocalTime():HH:mm}" : $"Quiet until {until.ToLocalTime():HH:mm}";
}
