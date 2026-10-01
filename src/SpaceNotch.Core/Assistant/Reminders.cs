using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpaceNotch.Core.Assistant;

/// <summary>Un rappel posé par une phrase (I2) ou tiré d'une copie (I3).</summary>
public sealed record Reminder(string Id, string Text, DateTimeOffset At);

/// <summary>
/// Le carnet des rappels : ajoutés, échus, retirés. Il se relit et s'écrit en
/// JSON pour survivre à un redémarrage ; une entrée illisible est ignorée.
/// </summary>
public sealed class ReminderBook
{
    /// <summary>Rappels gardés au plus.</summary>
    public const int Capacity = 50;

    private readonly List<Reminder> _items = [];

    public IReadOnlyList<Reminder> Items => [.. _items.OrderBy(r => r.At)];

    /// <summary>Le prochain rappel, ou <c>null</c>.</summary>
    public Reminder? Next => _items.OrderBy(r => r.At).FirstOrDefault();

    /// <summary>Ajoute un rappel ; le plus lointain part si le carnet est plein.</summary>
    public Reminder Add(string text, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var reminder = new Reminder(Guid.NewGuid().ToString("N")[..12], text.Trim(), at);
        _items.Add(reminder);

        if (_items.Count > Capacity)
        {
            _items.Remove(_items.OrderByDescending(r => r.At).First());
        }

        return reminder;
    }

    public bool Remove(string id) => _items.RemoveAll(r => r.Id == id) > 0;

    /// <summary>Retire et rend les rappels échus à <paramref name="now"/>.</summary>
    public IReadOnlyList<Reminder> TakeDue(DateTimeOffset now)
    {
        var due = _items.Where(r => r.At <= now).OrderBy(r => r.At).ToList();
        _items.RemoveAll(r => r.At <= now);
        return due;
    }

    public string Serialize()
    {
        var array = new JsonArray();

        foreach (Reminder r in _items.OrderBy(r => r.At))
        {
            array.Add(new JsonObject { ["id"] = r.Id, ["text"] = r.Text, ["at"] = r.At.ToString("O", System.Globalization.CultureInfo.InvariantCulture) });
        }

        return array.ToJsonString();
    }

    public static ReminderBook Parse(string? json)
    {
        var book = new ReminderBook();

        if (string.IsNullOrWhiteSpace(json))
        {
            return book;
        }

        try
        {
            if (JsonNode.Parse(json) is not JsonArray array)
            {
                return book;
            }

            foreach (JsonNode? node in array)
            {
                if (node is JsonObject o
                    && o["id"]?.GetValue<string>() is { Length: > 0 } id
                    && o["text"]?.GetValue<string>() is { Length: > 0 } text
                    && DateTimeOffset.TryParse(o["at"]?.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTimeOffset at)
                    && book._items.Count < Capacity)
                {
                    book._items.Add(new Reminder(id, text, at));
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            // Un carnet abîmé repart vide plutôt que d'empêcher le démarrage.
        }

        return book;
    }
}
