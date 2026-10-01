namespace SpaceNotch.Core.Calendar;

/// <summary>Durée retenue pour une session de focus, et pourquoi.</summary>
/// <param name="Duration">Durée à lancer ; zéro si la réunion est trop proche pour un focus utile.</param>
/// <param name="Shortened">Vrai si la durée a été raccourcie pour finir avant la réunion.</param>
/// <param name="Meeting">Titre de la réunion qui a raccourci la session.</param>
/// <param name="MeetingStart">Heure de cette réunion.</param>
public readonly record struct FocusFit(TimeSpan Duration, bool Shortened, string? Meeting, DateTimeOffset? MeetingStart);

/// <summary>
/// Focus calé sur l'agenda (W3) : une session de focus ne sonne jamais en
/// pleine réunion. Si le prochain rendez-vous tombe avant la fin, la session
/// est raccourcie pour finir deux minutes avant, le temps de se préparer ;
/// sous cinq minutes utiles, il n'y a pas de focus du tout.
/// </summary>
public static class FocusPlan
{
    /// <summary>Marge avant la réunion.</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromMinutes(2);

    /// <summary>En dessous, un focus n'a pas de sens.</summary>
    public static readonly TimeSpan Minimum = TimeSpan.FromMinutes(5);

    public static FocusFit Fit(TimeSpan requested, DateTimeOffset now, DateTimeOffset? meetingStart, string? meeting = null)
    {
        if (requested <= TimeSpan.Zero)
        {
            return new FocusFit(TimeSpan.Zero, false, null, null);
        }

        if (meetingStart is not { } start || start <= now || now + requested + Margin <= start)
        {
            return new FocusFit(requested, false, null, null);
        }

        TimeSpan available = start - Margin - now;

        // Arrondi à la minute inférieure : « 18 min » se lit, « 18 min 37 s » non.
        TimeSpan fitted = TimeSpan.FromMinutes(Math.Floor(available.TotalMinutes));
        return fitted < Minimum
            ? new FocusFit(TimeSpan.Zero, true, meeting, start)
            : new FocusFit(fitted, true, meeting, start);
    }
}
