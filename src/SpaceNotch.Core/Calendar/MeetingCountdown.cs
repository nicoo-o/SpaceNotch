namespace SpaceNotch.Core.Calendar;

/// <summary>Où en est un rendez-vous par rapport à maintenant.</summary>
public enum MeetingPhase
{
    /// <summary>Trop loin (ou fini) : rien à montrer.</summary>
    None,

    /// <summary>Dans moins de cinq minutes : la pastille compte à rebours.</summary>
    Soon,

    /// <summary>C'est l'heure : « Rejoindre ».</summary>
    Now
}

/// <summary>
/// Prochain rendez-vous (F2) : cinq minutes avant une réunion, une pastille
/// compte à rebours avec un anneau qui se vide ; à l'heure, le bouton
/// « Rejoindre » apparaît, et reste dix minutes (on arrive rarement pile).
/// </summary>
public static class MeetingCountdown
{
    public static readonly TimeSpan Lead = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);

    public static MeetingPhase Phase(DateTimeOffset start, DateTimeOffset end, DateTimeOffset now)
    {
        if (now < start - Lead)
        {
            return MeetingPhase.None;
        }

        if (now < start)
        {
            return MeetingPhase.Soon;
        }

        DateTimeOffset until = end > start ? (start + Grace < end ? start + Grace : end) : start + Grace;
        return now < until ? MeetingPhase.Now : MeetingPhase.None;
    }

    /// <summary>Part de l'anneau encore pleine : 1 à cinq minutes, 0 à l'heure.</summary>
    public static double Remaining(DateTimeOffset start, DateTimeOffset now)
        => Math.Clamp((start - now) / Lead, 0, 1);

    /// <summary>« dans 4 min », « dans 30 s », « maintenant ».</summary>
    public static string Label(DateTimeOffset start, DateTimeOffset now, bool french)
    {
        TimeSpan left = start - now;

        if (left <= TimeSpan.Zero)
        {
            return french ? "maintenant" : "now";
        }

        return left < TimeSpan.FromMinutes(1)
            ? (french ? $"dans {Math.Ceiling(left.TotalSeconds):0} s" : $"in {Math.Ceiling(left.TotalSeconds):0} s")
            : (french ? $"dans {Math.Ceiling(left.TotalMinutes):0} min" : $"in {Math.Ceiling(left.TotalMinutes):0} min");
    }

    /// <summary>Le temps restant, court, pour la droite de la pastille : « 4 min », « 30 s ».</summary>
    public static string TimeLeft(DateTimeOffset start, DateTimeOffset now)
    {
        TimeSpan left = start - now;

        return left <= TimeSpan.Zero
            ? "0 s"
            : left < TimeSpan.FromMinutes(1)
                ? $"{Math.Ceiling(left.TotalSeconds):0} s"
                : $"{Math.Ceiling(left.TotalMinutes):0} min";
    }

    /// <summary>Prochain instant où la phase change : là où réveiller la notch, sans scruter.</summary>
    public static DateTimeOffset? NextChange(DateTimeOffset start, DateTimeOffset end, DateTimeOffset now)
    {
        DateTimeOffset until = end > start ? (start + Grace < end ? start + Grace : end) : start + Grace;
        DateTimeOffset[] marks = [start - Lead, start, until];

        foreach (DateTimeOffset mark in marks)
        {
            if (mark > now)
            {
                return mark;
            }
        }

        return null;
    }
}
