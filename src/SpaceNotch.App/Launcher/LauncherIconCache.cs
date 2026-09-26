using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SpaceNotch.Platform.Windows.Launcher;

namespace SpaceNotch_App.Launcher;

/// <summary>
/// Les vraies icônes des résultats, extraites par le Shell sur un fil STA qui
/// leur est dédié, puis remises au fil de l'interface. Une icône n'est extraite
/// qu'une fois par session ; une icône introuvable est retenue aussi, pour ne
/// pas la redemander à chaque lettre.
/// </summary>
internal sealed class LauncherIconCache : IDisposable
{
    /// <summary>Extraite en 48 px : nette à 24 DIPs jusqu'à 200 %.</summary>
    private const int PixelSize = 48;

    private const int Capacity = 400;

    private readonly DispatcherQueue _dispatcher;
    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Action<ImageSource>>> _waiting = new(StringComparer.OrdinalIgnoreCase);
    private readonly BlockingCollection<string> _queue = new();
    private readonly Thread _worker;

    public LauncherIconCache(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _worker = new Thread(Work) { IsBackground = true, Name = "SpaceNotch.Icons" };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    /// <summary>
    /// Donne l'icône tout de suite si elle est connue ; sinon la demande et
    /// appelle <paramref name="ready"/> sur le fil de l'interface dès qu'elle
    /// arrive. Renvoie <c>null</c> en attendant, ou si elle n'existe pas.
    /// </summary>
    public ImageSource? Get(string path, Action<ImageSource> ready)
    {
        if (_cache.TryGetValue(path, out ImageSource? known))
        {
            return known;
        }

        if (_waiting.TryGetValue(path, out List<Action<ImageSource>>? callbacks))
        {
            callbacks.Add(ready);
            return null;
        }

        _waiting[path] = [ready];

        if (!_queue.IsAddingCompleted)
        {
            _queue.Add(path);
        }

        return null;
    }

    private void Work()
    {
        foreach (string path in _queue.GetConsumingEnumerable())
        {
            IconPixels? pixels = null;

            try
            {
                pixels = IconLoader.Load(path, PixelSize);
            }
            catch (Exception)
            {
                // Une icône qui ne vient pas n'empêche pas le résultat : le
                // glyphe de repli prend sa place.
            }

            _dispatcher.TryEnqueue(() => Deliver(path, pixels));
        }
    }

    private void Deliver(string path, IconPixels? pixels)
    {
        ImageSource? image = null;

        if (pixels is not null)
        {
            try
            {
                var bitmap = new WriteableBitmap(pixels.Width, pixels.Height);
                pixels.Bgra.CopyTo(bitmap.PixelBuffer);
                bitmap.Invalidate();
                image = bitmap;
            }
            catch (Exception)
            {
                image = null;
            }
        }

        if (_cache.Count >= Capacity)
        {
            _cache.Clear();
        }

        _cache[path] = image;

        if (_waiting.Remove(path, out List<Action<ImageSource>>? callbacks) && image is not null)
        {
            foreach (Action<ImageSource> callback in callbacks)
            {
                callback(image);
            }
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
    }
}
