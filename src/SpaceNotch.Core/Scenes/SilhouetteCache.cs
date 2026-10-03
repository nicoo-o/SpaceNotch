using System;
using System.Collections.Generic;

namespace SpaceNotch.Core.Scenes;

/// <summary>
/// Cache des contours (phase D) : pendant un ressort, la forme repasse sans
/// cesse par les mêmes tailles — ouverture, rebond, fermeture, survol. Les
/// dimensions sont arrondies au quart de DIP (en dessous de ce que l'œil
/// distingue) et les derniers contours calculés sont gardés, du plus récent
/// au plus ancien.
/// </summary>
public sealed class SilhouetteCache
{
    /// <summary>Pas d'arrondi des dimensions, en DIP.</summary>
    public const double Step = 0.25;

    private readonly int _capacity;
    private readonly Dictionary<Key, LinkedListNode<(Key Key, ShapePoint[] Points)>> _index = [];
    private readonly LinkedList<(Key Key, ShapePoint[] Points)> _order = new();

    public SilhouetteCache(int capacity = 96)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    /// <summary>Nombre de contours gardés.</summary>
    public int Count => _index.Count;

    /// <summary>Contours calculés (absents du cache).</summary>
    public long Misses { get; private set; }

    /// <summary>Contours servis depuis le cache.</summary>
    public long Hits { get; private set; }

    /// <summary>Dimension arrondie au pas du cache.</summary>
    public static double Snap(double value) => Math.Round(value / Step) * Step;

    /// <summary>Contour accroché, calculé ou repris du cache.</summary>
    public ShapePoint[] Silhouette(double width, double height, double radius, double smoothing, double band, double shoulder)
        => Get(
            new Key(false, Snap(width), Snap(height), Snap(radius), Math.Round(smoothing, 2), Snap(band), Snap(shoulder)),
            k => IslandShape.Silhouette(k.Width, k.Height, k.Radius, k.Smoothing, k.Band, k.Shoulder));

    /// <summary>Contour de la pastille détachée, calculé ou repris du cache.</summary>
    public ShapePoint[] Floating(double width, double height, double radius, double smoothing)
        => Get(
            new Key(true, Snap(width), Snap(height), Snap(radius), Math.Round(smoothing, 2), 0, 0),
            k => IslandShape.Floating(k.Width, k.Height, k.Radius, k.Smoothing));

    /// <summary>Vide le cache (changement de géométrie dans les réglages).</summary>
    public void Clear()
    {
        _index.Clear();
        _order.Clear();
    }

    private ShapePoint[] Get(Key key, Func<Key, ShapePoint[]> compute)
    {
        if (_index.TryGetValue(key, out LinkedListNode<(Key Key, ShapePoint[] Points)>? node))
        {
            Hits++;
            _order.Remove(node);
            _order.AddFirst(node);
            return node.Value.Points;
        }

        Misses++;
        ShapePoint[] points = compute(key);
        _index[key] = _order.AddFirst((key, points));

        if (_index.Count > _capacity)
        {
            LinkedListNode<(Key Key, ShapePoint[] Points)> last = _order.Last!;
            _order.RemoveLast();
            _index.Remove(last.Value.Key);
        }

        return points;
    }

    private readonly record struct Key(bool IsFloating, double Width, double Height, double Radius, double Smoothing, double Band, double Shoulder);
}
