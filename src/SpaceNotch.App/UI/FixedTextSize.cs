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
/// (design-language.md, « Espacement ») : un texte agrandi y serait rogné. Les Réglages et l'installation,
/// eux, suivent la taille du texte de Windows.
/// </summary>
/// <remarks>
/// WinUI agrandit le texte élément par élément, sans réglage pour toute une
/// fenêtre. Les scènes figent leur contenu dès qu'il est posé (<see cref="Apply"/>,
/// avant d'être mesuré) ; un filet parcourt aussi l'arbre après une mise en page,
/// au plus toutes les 250 ms, pour ce qui naît ailleurs. Seulement si le texte
/// est agrandi : à 100 %, rien n'est fait.
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

    /// <summary>Délai du filet : un ressort de 30 images ne coûte qu'un ou deux parcours.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Filet : après une mise en page, un parcours au plus toutes les 250 ms. Sans
    /// délai, chaque image d'un ressort postait un parcours de tout l'arbre.
    /// </summary>
    /// <returns>Le minuteur du filet, à suivre et arrêter avec la fenêtre.</returns>
    public static DispatcherQueueTimer Keep(FrameworkElement root, DispatcherQueue queue)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(queue);

        DispatcherQueueTimer timer = queue.CreateTimer();
        timer.Interval = Settle;
        timer.IsRepeating = false;
        timer.OnTick((_, _) => Apply(root));

        root.LayoutUpdated += (_, _) =>
        {
            if (!timer.IsRunning && Factor > 1)
            {
                timer.Start();
            }
        };

        return timer;
    }

    /// <summary>
    /// Un passage sur l'arbre : textes et contrôles cessent de suivre la taille du
    /// texte. Rien n'est fait à 100 % ; une valeur déjà figée n'est pas réécrite.
    /// </summary>
    public static void Apply(DependencyObject node)
    {
        if (Factor > 1)
        {
            Walk(node);
        }
    }

    private static void Walk(DependencyObject node)
    {
        try
        {
            switch (node)
            {
                case TextBlock { IsTextScaleFactorEnabled: true } text:
                    text.IsTextScaleFactorEnabled = false;
                    break;

                case RichTextBlock { IsTextScaleFactorEnabled: true } rich:
                    rich.IsTextScaleFactorEnabled = false;
                    break;

                case ContentPresenter { IsTextScaleFactorEnabled: true } presenter:
                    presenter.IsTextScaleFactorEnabled = false;
                    break;

                case Control { IsTextScaleFactorEnabled: true } control:
                    control.IsTextScaleFactorEnabled = false;
                    break;
            }

            int count = VisualTreeHelper.GetChildrenCount(node);

            for (int i = 0; i < count; i++)
            {
                Walk(VisualTreeHelper.GetChild(node, i));
            }
        }
        catch (Exception ex)
        {
            // Un élément détaché pendant le parcours : le prochain passage reprendra.
            SpaceNotch.Infrastructure.Logging.MiniLogger.Log("[TEXTE] Taille figée interrompue", ex);
        }
    }
}
