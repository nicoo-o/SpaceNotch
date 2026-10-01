namespace SpaceNotch.Core.Channel;

/// <summary>
/// Barre à étapes (W1) : une progression découpée en segments, comme les Live
/// Updates d'Android 16. Chaque segment se remplit à son tour ; l'avancement
/// d'ensemble en découle.
/// </summary>
public static class ProgressSteps
{
    /// <summary>Avancement d'ensemble, de 0 à 1.</summary>
    public static double Overall(ProgressMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.State == ChannelState.Done)
        {
            return 1;
        }

        double inStep = message.Fraction ?? 0;

        if (message.Steps <= 0)
        {
            return Math.Clamp(inStep, 0, 1);
        }

        return Math.Clamp(((message.Step - 1) + inStep) / message.Steps, 0, 1);
    }

    /// <summary>Remplissage de chaque segment, de 0 à 1 ; un seul segment sans étapes.</summary>
    public static IReadOnlyList<double> Segments(ProgressMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Steps <= 0)
        {
            return [Overall(message)];
        }

        var fills = new double[message.Steps];

        for (int i = 0; i < fills.Length; i++)
        {
            int step = i + 1;
            fills[i] = message.State == ChannelState.Done || step < message.Step ? 1
                : step == message.Step ? Math.Clamp(message.Fraction ?? 0, 0, 1)
                : 0;
        }

        return fills;
    }

    /// <summary>Texte de la mesure à droite : « 3/4 », « 62 % », « ✓ ».</summary>
    public static string Metric(ProgressMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.State switch
        {
            ChannelState.Done => "✓",
            ChannelState.Error => "!",
            _ when message.Steps > 0 => $"{message.Step}/{message.Steps}",
            _ when message.Fraction is { } f => $"{Math.Round(f * 100):0} %",
            _ => "…"
        };
    }
}
