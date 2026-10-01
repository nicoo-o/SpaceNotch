using System.Globalization;
using System.Text.RegularExpressions;
using SpaceNotch.Core.Activities;

namespace SpaceNotch.Core.Launcher;

/// <summary>Ce qu'une commande tapée fait.</summary>
public enum LauncherCommandKind
{
    /// <summary>Lance un minuteur.</summary>
    Timer,

    /// <summary>Règle le volume.</summary>
    Volume,

    /// <summary>Copie une couleur (et l'affiche, par le presse-papier).</summary>
    Color,

    /// <summary>Capture de texte (W4) : tracer un rectangle, le texte part au presse-papier.</summary>
    Capture
}

/// <summary>Une commande reconnue, avec ce que la ligne de résultat en montre.</summary>
public sealed record LauncherCommand(LauncherCommandKind Kind, string Title, string Subtitle, string Target);

/// <summary>
/// Commandes tapées (F4) : dans Alt+Espace, une commande courte montre son
/// résultat avant Entrée — « timer 10 » prépare un minuteur de dix minutes,
/// « vol 30 » le niveau, « #7FE6FF » la couleur. L'analyse est locale, sans
/// réseau ; la commande devient une ligne de résultat comme une autre, en
/// tête. La cible est une adresse <c>cmd:</c> que l'application exécute.
/// </summary>
public static partial class LauncherCommands
{
    public const string Prefix = "cmd:";

    /// <summary>Durée maximale d'un minuteur lancé ainsi.</summary>
    public static readonly TimeSpan MaxTimer = TimeSpan.FromHours(24);

    public static bool TryParse(string? query, bool french, out LauncherCommand command)
    {
        command = null!;
        string q = (query ?? string.Empty).Trim();

        if (q.Length == 0)
        {
            return false;
        }

        Match timer = TimerPattern().Match(q);

        if (timer.Success)
        {
            double amount = double.Parse(timer.Groups["n"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            bool seconds = timer.Groups["u"].Value.StartsWith('s');
            bool hours = timer.Groups["u"].Value.StartsWith('h');
            TimeSpan duration = seconds ? TimeSpan.FromSeconds(amount) : hours ? TimeSpan.FromHours(amount) : TimeSpan.FromMinutes(amount);

            if (duration <= TimeSpan.Zero || duration > MaxTimer)
            {
                return false;
            }

            string shown = duration.TotalHours >= 1
                ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                : duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

            command = new LauncherCommand(
                LauncherCommandKind.Timer,
                (french ? "Minuteur " : "Timer ") + shown,
                french ? "Entrée pour lancer" : "Enter to start",
                Prefix + "timer:" + ((int)duration.TotalSeconds).ToString(CultureInfo.InvariantCulture));
            return true;
        }

        Match volume = VolumePattern().Match(q);

        if (volume.Success)
        {
            int level = int.Parse(volume.Groups["n"].Value, CultureInfo.InvariantCulture);

            if (level is < 0 or > 100)
            {
                return false;
            }

            command = new LauncherCommand(
                LauncherCommandKind.Volume,
                $"Volume {level} %",
                french ? "Entrée pour régler" : "Enter to set",
                Prefix + "volume:" + level.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        // Une couleur s'écrit avec un dièse ou une fonction : « 7FE6FF » seul
        // reste une recherche, il pourrait être un nom de fichier.
        if ((q.StartsWith('#') || q.StartsWith("rgb", StringComparison.OrdinalIgnoreCase) || q.StartsWith("hsl", StringComparison.OrdinalIgnoreCase))
            && ColorCode.TryParse(q, out ColorCode color))
        {
            command = new LauncherCommand(
                LauncherCommandKind.Color,
                color.Hex,
                color.Rgb + " · " + (french ? "Entrée pour copier" : "Enter to copy"),
                Prefix + "color:" + color.Hex.TrimStart('#'));
            return true;
        }

        if (CapturePattern().IsMatch(q))
        {
            command = new LauncherCommand(
                LauncherCommandKind.Capture,
                french ? "Capturer du texte" : "Capture text",
                french ? "Tracer un rectangle · le texte est copié" : "Draw a rectangle · the text is copied",
                Prefix + "capture:text");
            return true;
        }

        return false;
    }

    /// <summary>Lit une cible <c>cmd:</c> : le genre et sa valeur.</summary>
    public static bool TryRead(string? target, out LauncherCommandKind kind, out string value)
    {
        kind = default;
        value = string.Empty;

        if (target is null || !target.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = target[Prefix.Length..].Split(':', 2);

        if (parts.Length != 2)
        {
            return false;
        }

        value = parts[1];
        switch (parts[0])
        {
            case "timer": kind = LauncherCommandKind.Timer; return true;
            case "volume": kind = LauncherCommandKind.Volume; return true;
            case "color": kind = LauncherCommandKind.Color; return true;
            case "capture": kind = LauncherCommandKind.Capture; return true;
            default: return false;
        }
    }

    [GeneratedRegex(@"^(?:ocr|texte|text|capture|capturer(?: du)? texte|capture text|copier (?:le )?texte)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CapturePattern();

    [GeneratedRegex(@"^(?:timer|minuteur|min)\s+(?<n>\d{1,4}(?:[.,]\d+)?)\s*(?<u>s|sec|secondes?|seconds?|m|min|minutes?|h|heures?|hours?)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimerPattern();

    [GeneratedRegex(@"^(?:vol|volume)\s+(?<n>\d{1,3})\s*%?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VolumePattern();
}
