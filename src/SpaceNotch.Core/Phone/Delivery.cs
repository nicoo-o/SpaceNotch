using System.Globalization;
using System.Text.RegularExpressions;

namespace SpaceNotch.Core.Phone;

/// <summary>Ce qui arrive : un repas, une voiture, un colis.</summary>
public enum DeliveryKind
{
    Food,
    Ride,
    Parcel
}

/// <summary>Les trois étapes montrées sur la frise.</summary>
public enum DeliveryStep
{
    /// <summary>Commande acceptée, en préparation, chauffeur recherché.</summary>
    Preparing = 0,

    /// <summary>En route.</summary>
    OnTheWay = 1,

    /// <summary>Arrivé, devant la porte.</summary>
    Arrived = 2
}

/// <summary>Une étape de livraison lue dans une notification.</summary>
public sealed record DeliveryUpdate(string Service, DeliveryKind Kind, DeliveryStep Step, DateTimeOffset? Eta);

/// <summary>
/// Livraisons et VTC (T2) : Uber Eats, Deliveroo, Uber, Bolt… Leurs
/// notifications, relayées par le téléphone ou venues d'une appli Windows,
/// donnent l'étape et souvent l'heure d'arrivée. Lecture locale, par mots-clés.
/// </summary>
public static partial class Delivery
{
    private static readonly (string Name, DeliveryKind Kind)[] Services =
    [
        // Les noms composés d'abord : « Uber Eats » n'est pas « Uber ».
        ("Uber Eats", DeliveryKind.Food),
        ("Deliveroo", DeliveryKind.Food),
        ("Just Eat", DeliveryKind.Food),
        ("Glovo", DeliveryKind.Food),
        ("DoorDash", DeliveryKind.Food),
        ("Uber", DeliveryKind.Ride),
        ("Bolt", DeliveryKind.Ride),
        ("Heetch", DeliveryKind.Ride),
        ("FREENOW", DeliveryKind.Ride),
        ("Free Now", DeliveryKind.Ride),
        ("G7", DeliveryKind.Ride),
        ("Amazon", DeliveryKind.Parcel),
        ("Chronopost", DeliveryKind.Parcel),
        ("Colissimo", DeliveryKind.Parcel),
        ("DPD", DeliveryKind.Parcel),
        ("DHL", DeliveryKind.Parcel),
        ("UPS", DeliveryKind.Parcel)
    ];

    private static readonly string[] ArrivedWords =
    [
        "est arrivé", "est arrivée", "arrivé à destination", "est là", "est devant", "à votre porte", "à ta porte", "livré", "livrée",
        "has arrived", "is here", "is outside", "arrived", "at your door", "delivered"
    ];

    private static readonly string[] OnTheWayWords =
    [
        "en route", "en chemin", "approche", "arrive dans", "arrivera", "récupéré", "en cours de livraison", "en livraison", "est parti",
        "on the way", "on its way", "arriving", "picked up", "out for delivery", "heading to you", "is nearby"
    ];

    private static readonly string[] PreparingWords =
    [
        "en préparation", "prépare", "commande acceptée", "commande confirmée", "recherche d'un", "cherche un", "confirmé",
        "preparing", "order accepted", "order confirmed", "finding", "looking for", "being prepared", "confirmed"
    ];

    /// <summary>Le service nommé dans l'application ou le titre, s'il est connu.</summary>
    public static (string Name, DeliveryKind Kind)? Service(string? appName, string? title)
    {
        string text = (appName ?? string.Empty) + " · " + (title ?? string.Empty);

        foreach ((string name, DeliveryKind kind) in Services)
        {
            if (Regex.IsMatch(text, @"(?<![\p{L}\d])" + Regex.Escape(name) + @"(?![\p{L}\d])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return (name == "FREENOW" ? "Free Now" : name, kind);
            }
        }

        return null;
    }

    /// <summary>Une étape de livraison, si la notification en décrit une.</summary>
    public static DeliveryUpdate? Read(string? appName, string? title, string? body, DateTimeOffset now)
    {
        if (Service(appName, title) is not { } service)
        {
            return null;
        }

        string text = ((title ?? string.Empty) + " " + (body ?? string.Empty)).ToLowerInvariant();

        // L'étape la plus avancée gagne : « est arrivé » l'emporte sur « en route ».
        DeliveryStep? step = ArrivedWords.Any(text.Contains) ? DeliveryStep.Arrived
            : OnTheWayWords.Any(text.Contains) ? DeliveryStep.OnTheWay
            : PreparingWords.Any(text.Contains) ? DeliveryStep.Preparing
            : null;

        if (step is not { } s)
        {
            return null;
        }

        return new DeliveryUpdate(service.Name, service.Kind, s, s == DeliveryStep.Arrived ? null : Eta(text, now));
    }

    /// <summary>« dans 12 min », « in 5 minutes », « 12-18 min », « à 19:42 », « 19h42 », « at 7:42 PM ».</summary>
    public static DateTimeOffset? Eta(string text, DateTimeOffset now)
    {
        Match minutes = MinutesPattern().Match(text);

        if (minutes.Success && int.TryParse(minutes.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n is > 0 and <= 240)
        {
            return now.AddMinutes(n);
        }

        Match clock = ClockPattern().Match(text);

        if (clock.Success
            && int.TryParse(clock.Groups["h"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)
            && int.TryParse(clock.Groups["m"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int m)
            && h <= 23 && m <= 59)
        {
            string ampm = clock.Groups["ap"].Value.ToLowerInvariant();

            if (ampm == "pm" && h < 12)
            {
                h += 12;
            }
            else if (ampm == "am" && h == 12)
            {
                h = 0;
            }

            var at = new DateTimeOffset(now.Year, now.Month, now.Day, h, m, 0, now.Offset);

            // Une heure déjà passée de plus d'une heure : c'est demain (une livraison à 00:10).
            return at < now.AddHours(-1) ? at.AddDays(1) : at;
        }

        return null;
    }

    /// <summary>Où poser le véhicule sur la frise, de 0 à 1.</summary>
    public static double Position(DeliveryStep step, DateTimeOffset? eta, DateTimeOffset now, DateTimeOffset? since)
    {
        switch (step)
        {
            case DeliveryStep.Preparing:
                return 0.08;
            case DeliveryStep.Arrived:
                return 1;
        }

        // En route : du milieu vers l'arrivée, à mesure que l'heure approche.
        if (eta is { } e && since is { } s && e > s)
        {
            double done = (now - s).TotalSeconds / (e - s).TotalSeconds;
            return 0.5 + (Math.Clamp(done, 0, 1) * 0.42);
        }

        return 0.5;
    }

    /// <summary>« En préparation », « En route », « Arrivé » — selon ce qui arrive.</summary>
    public static string StepLabel(DeliveryStep step, DeliveryKind kind, bool french) => (step, kind) switch
    {
        (DeliveryStep.Preparing, DeliveryKind.Ride) => french ? "Chauffeur en approche" : "Driver assigned",
        (DeliveryStep.Preparing, _) => french ? "En préparation" : "Preparing",
        (DeliveryStep.OnTheWay, DeliveryKind.Ride) => french ? "Chauffeur en route" : "Driver on the way",
        (DeliveryStep.OnTheWay, _) => french ? "En route" : "On the way",
        (DeliveryStep.Arrived, DeliveryKind.Ride) => french ? "Ton chauffeur est là" : "Your driver is here",
        _ => french ? "Arrivé" : "Arrived"
    };

    /// <summary>Le motif en pixels du véhicule.</summary>
    public static string VehicleGlyph(DeliveryKind kind) => kind switch
    {
        DeliveryKind.Ride => "Car",
        DeliveryKind.Parcel => "Parcel",
        _ => "Scooter"
    };

    [GeneratedRegex(@"(?:dans|in|d'ici|within)\s+(?:environ\s+|about\s+|~)?(?<n>\d{1,3})(?:\s*[-–à]\s*\d{1,3})?\s*(?:min|minutes?|mn)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MinutesPattern();

    [GeneratedRegex(@"(?:à|at|vers|around|pour|by)\s+(?<h>\d{1,2})\s*(?:[:h])\s*(?<m>\d{2})\s*(?<ap>am|pm)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClockPattern();
}
