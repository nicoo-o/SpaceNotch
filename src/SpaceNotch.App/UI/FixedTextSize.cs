using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace SpaceNotch_App.UI;

/// <summary>
/// La notch garde la taille de son texte quand Windows l'agrandit (n° 59, choix
/// de l'auteur). Ses formes sont des sommes de hauteurs de ligne fixes
/// (ADR-012) : un texte agrandi y serait rogné. Les Réglages et l'installation,
/// eux, suivent la taille du texte de Windows.
/// </summary>
/// <remarks>
/// WinUI agrandit le texte élément par élément, sans réglage pour toute une
/// fenêtre : l'arbre visuel est donc parcouru après chaque mise en page, au plus
/// une fois par tour du répartiteur, et seulement si le texte est agrandi. À
/// 100 %, rien n'est fait.
/// </remarks>
public static class FixedTextSize
{
    private static UISettings? _settings;
    private static double _factor = 1;

    /// <summary>Facteur de taille du texte de Windows (1 à 2,25).</summary>
    public static double Factor
    {
        get
        {
            if (_settings is null)
            {
                try
                {
                    _settings = new UISettings();
                    _factor = _settings.TextScaleFactor;
                    _settings.TextScaleFactorChanged += (sender, _) => _factor = sender.TextScaleFactor;
                }
                catch (Exception)
                {
                    _factor = 1;
                }
            }

            return _factor;
        }
    }

    /// <summary>Fige la taille du texte sous <paramref name="root"/>, maintenant et à chaque mise en page.</summary>
    public static void Keep(FrameworkElement root, DispatcherQueue queue)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(queue);

        bool queued = false;

        root.LayoutUpdated += (_, _) =>
        {
            if (queued || Factor <= 1)
            {
                return;
            }

            queued = true;
            _ = queue.TryEnqueueSafely(DispatcherQueuePriority.Low, () =>
            {
                queued = false;
                Apply(root);
            });
        };
    }

    /// <summary>Un passage sur l'arbre : textes et contrôles cessent de suivre la taille du texte.</summary>
    public static void Apply(DependencyObject node)
    {
        try
        {
            switch (node)
            {
                case TextBlock text:
                    text.IsTextScaleFactorEnabled = false;
                    break;

                case RichTextBlock rich:
                    rich.IsTextScaleFactorEnabled = false;
                    break;

                case Control control:
                    control.IsTextScaleFactorEnabled = false;
                    break;
            }

            int count = VisualTreeHelper.GetChildrenCount(node);

            for (int i = 0; i < count; i++)
            {
                Apply(VisualTreeHelper.GetChild(node, i));
            }
        }
        catch (Exception ex)
        {
            // Un élément détaché pendant le parcours : le prochain passage reprendra.
            SpaceNotch.Infrastructure.Logging.MiniLogger.Log("[TEXTE] Taille figée interrompue", ex);
        }
    }
}
