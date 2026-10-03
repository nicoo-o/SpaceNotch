using System;
using System.Globalization;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SpaceNotch.Core.Activities;
using SpaceNotch.Features.Menu;
using SpaceNotch_App.UI;
using Windows.System;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace SpaceNotch_App.Views.Scenes;

/// <summary>
/// Présentation du premier lancement, dans la notch : une carte par geste,
/// une petite illustration animée au-dessus du texte. La dernière carte
/// demande l'accès aux notifications — ou dit pourquoi elle ne le peut pas.
/// </summary>
public sealed partial class WelcomeScene : UserControl, IIslandSceneView
{
    private static readonly bool French = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";

    private const double CanvasWidth = 402;
    private const double CanvasHeight = 94;

    private string? _activityId;
    private WelcomePayload? _shown;

    public WelcomeScene()
    {
        InitializeComponent();
    }

    public event EventHandler<IslandActionRequest>? ActionRequested;

    public FrameworkElement Root => this;

    /// <summary>Le bouton principal prend le clavier : Entrée avance, Échap passe.</summary>
    public void FocusPrimary() => PrimaryButton.Focus(FocusState.Programmatic);

    public void Apply(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        _activityId = activity.Id;

        if (activity.Payload is not WelcomePayload payload || payload == _shown)
        {
            return;
        }

        bool stepChanged = _shown is null || _shown.Step != payload.Step;
        _shown = payload;

        (string title, string description) = Texts(payload);
        TitleText.Text = title;
        DescriptionText.Text = description;

        BuildDots(payload);
        BuildButtons(payload);

        if (stepChanged)
        {
            BuildIllustration(payload.Step);
            PlayStepEntrance();
        }
    }

    // ------------------------------------------------------------------
    // Textes et boutons
    // ------------------------------------------------------------------

    private static (string Title, string Description) Texts(WelcomePayload payload) => payload.Step switch
    {
        0 => (French ? "Bienvenue dans SpaceNotch" : "Welcome to SpaceNotch",
              French ? "Survole la notch pour un aperçu, clique pour l'ouvrir. Échap ou un clic ailleurs la referme."
                     : "Hover the notch for a peek, click to open it. Esc or a click elsewhere closes it."),
        1 => (French ? "Clic droit : le menu rapide" : "Right-click: the quick menu",
              French ? "Minuteur, presse-papier, détacher la notch, réglages — tout est à un clic droit."
                     : "Timer, clipboard, detach, settings — all one right-click away."),
        2 => (French ? "Cherche tout, de partout" : "Search everything, from anywhere",
              French ? "Applications, réglages Windows, fichiers, calculs. Ctrl+K sur un résultat pour plus d'actions."
                     : "Apps, Windows settings, files, math. Ctrl+K on a result for more actions."),
        3 => (French ? "Tire-la pour la détacher" : "Pull it to detach",
              French ? "La notch devient une pastille que tu poses où tu veux. Double-clic pour la raccrocher."
                     : "The notch becomes a pill you can drop anywhere. Double-click to dock it again."),
        _ => (French ? "Tes notifications, ici" : "Your notifications, here", payload.NotificationAccess switch
        {
            "allowed" => French ? "C'est déjà autorisé : elles apparaîtront dans la notch, puis se rangeront."
                                : "Already allowed: they'll show up in the notch, then tuck away.",
            "denied" => French ? "Windows les bloque pour SpaceNotch. Réglages › Général te montre comment les rouvrir."
                               : "Windows blocks them for SpaceNotch. Settings › General shows how to re-enable them.",
            "unavailable" => French ? "Elles demandent SpaceNotch installé, pas la version portable. Tu pourras les activer après l'installation."
                                    : "They need the installed SpaceNotch, not the portable one. You can turn them on after installing.",
            _ => French ? "Windows va te demander ton accord une fois. Tu pourras changer d'avis dans les réglages."
                        : "Windows will ask for your consent once. You can change your mind in Settings."
        })
    };

    private void BuildButtons(WelcomePayload payload)
    {
        bool ask = payload.IsLast && payload.NotificationAccess == "notasked";

        PrimaryButton.Content = !payload.IsLast
            ? (French ? "Suivant" : "Next")
            : ask ? (French ? "Autoriser" : "Allow") : (French ? "Terminer" : "Done");

        SecondaryButton.Content = ask ? (French ? "Plus tard" : "Later") : (French ? "Passer" : "Skip");
        SecondaryButton.Visibility = payload.IsLast && !ask ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BuildDots(WelcomePayload payload)
    {
        Dots.Children.Clear();

        for (int i = 0; i < payload.Count; i++)
        {
            bool on = i == payload.Step;
            Dots.Children.Add(new Border
            {
                Width = on ? 16 : 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = on ? Brush("NfActiveBrush") : new SolidColorBrush(ColorHelper.FromArgb(0x33, 0xFF, 0xFF, 0xFF))
            });
        }
    }

    private void OnPrimaryClicked(object sender, RoutedEventArgs e)
    {
        if (_shown is { IsLast: true, NotificationAccess: "notasked" })
        {
            Raise(WelcomeFeature.AllowAction);
            return;
        }

        Raise(WelcomeFeature.NextAction);
    }

    private void OnSecondaryClicked(object sender, RoutedEventArgs e) => Raise(WelcomeFeature.SkipAction);

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            OnPrimaryClicked(sender, e);
        }
        else if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)
        {
            // Les flèches feraient défiler les activités de la notch.
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------
    // Illustrations
    // ------------------------------------------------------------------

    private void BuildIllustration(int step)
    {
        Illustration.Children.Clear();

        switch (step)
        {
            case 0:
                Place(MiniNotch(70, 13, 8), 62, -1);
                UIElement cursor = Place(Cursor(), 98, 12);
                Place(Label("→", 18, "NfTextTertiaryBrush"), 172, 16);
                Place(PlayingNotch(), 214, -1);
                Place(Label(French ? "survol → aperçu · clic → ouvrir" : "hover → peek · click → open", 11, "NfTextTertiaryBrush"), 12, CanvasHeight - 24);
                Loop(cursor, "Translation", [Vector3.Zero, new Vector3(8, 4, 0), Vector3.Zero], 1.6);
                break;

            case 1:
                Border menu = MiniNotch(150, 76, 12);
                var lines = new StackPanel { Padding = new Thickness(10, 8, 10, 0), Spacing = 3 };
                lines.Children.Add(Label(French ? "⌕  Rechercher" : "⌕  Search", 10.5, "NfTextPrimaryBrush"));
                lines.Children.Add(Label(French ? "◷  Minuteur" : "◷  Timer", 10.5, "NfTextSecondaryBrush"));
                lines.Children.Add(Label(French ? "▭  Presse-papier" : "▭  Clipboard", 10.5, "NfTextSecondaryBrush"));
                menu.Child = lines;
                Place(menu, (CanvasWidth - 150) / 2, -1);
                var ring = new Ellipse { Width = 22, Height = 22, Stroke = Brush("NfActiveBrush"), StrokeThickness = 1.5 };
                Place(ring, 232, 14);
                Place(Cursor(), 238, 20);
                Loop(ring, "Scale", [new Vector3(0.7f, 0.7f, 1), new Vector3(1.25f, 1.25f, 1), new Vector3(0.7f, 0.7f, 1)], 1.4, center: new Vector3(11, 11, 0));
                break;

            case 2:
                var keys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                Border alt = KeyCap("Alt", 44);
                Border space = KeyCap(French ? "Espace" : "Space", 96);
                keys.Children.Add(alt);
                keys.Children.Add(Label("+", 14, "NfTextTertiaryBrush"));
                keys.Children.Add(space);
                Place(keys, (CanvasWidth - 170) / 2, (CanvasHeight - 32) / 2);
                Loop(alt, "Scale", [Vector3.One, new Vector3(0.92f, 0.92f, 1), Vector3.One, Vector3.One], 1.2, center: new Vector3(22, 16, 0));
                Loop(space, "Scale", [Vector3.One, Vector3.One, new Vector3(0.94f, 0.94f, 1), Vector3.One], 1.2, center: new Vector3(48, 16, 0));
                break;

            case 3:
                Border ghost = MiniNotch(90, 16, 8);
                ghost.Opacity = 0.35;
                Place(ghost, (CanvasWidth - 90) / 2, -1);
                var trail = new Path
                {
                    Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), "M 201,18 C 214,30 224,40 238,50"),
                    Stroke = Brush("NfActiveBrush"),
                    StrokeThickness = 1.5,
                    StrokeDashArray = [2, 3]
                };
                Place(trail, 0, 0);
                var pill = new Border
                {
                    Width = 74,
                    Height = 26,
                    CornerRadius = new CornerRadius(13),
                    Background = new SolidColorBrush(Colors.Black),
                    BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1)
                };
                Place(pill, 240, 44);
                Place(Cursor(), 292, 58);
                Loop(pill, "Translation", [new Vector3(-36, -30, 0), Vector3.Zero, Vector3.Zero, new Vector3(-36, -30, 0)], 2.4);
                break;

            default:
                Place(NotificationCard(), (CanvasWidth - 250) / 2, 14);
                break;
        }
    }

    private UIElement Place(UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        Illustration.Children.Add(element);
        return element;
    }

    private static Border MiniNotch(double width, double height, double radius) => new()
    {
        Width = width,
        Height = height,
        Background = new SolidColorBrush(Colors.Black),
        CornerRadius = new CornerRadius(0, 0, radius, radius),
        BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
        BorderThickness = new Thickness(1, 0, 1, 1)
    };

    private static Border PlayingNotch()
    {
        Border notch = MiniNotch(150, 50, 14);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(6),
            Background = new LinearGradientBrush(
                [new GradientStop { Color = ColorHelper.FromArgb(0xFF, 0xFF, 0x00, 0x66), Offset = 0 }, new GradientStop { Color = ColorHelper.FromArgb(0xFF, 0xFF, 0x99, 0x00), Offset = 1 }],
                45)
        });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Label("Midnight City", 10, "NfTextPrimaryBrush"));
        texts.Children.Add(Label("M83", 10, "NfTextTertiaryBrush"));
        row.Children.Add(texts);
        notch.Child = row;
        return notch;
    }

    private static Border NotificationCard()
    {
        var card = new Border
        {
            Width = 250,
            Height = 66,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Colors.Black),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 0, 12, 0)
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x58, 0x65, 0xF2)),
            Child = new TextBlock { Text = "D", FontWeight = FontWeights.Bold, FontSize = 13, Foreground = new SolidColorBrush(Colors.White), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        TextBlock sender = Label("Léa · Discord", 11, "NfTextPrimaryBrush");
        sender.FontWeight = FontWeights.SemiBold;
        texts.Children.Add(sender);
        texts.Children.Add(Label(French ? "On se retrouve à 19 h ?" : "Meet at 7 pm?", 11, "NfTextSecondaryBrush"));
        row.Children.Add(texts);
        card.Child = row;
        return card;
    }

    private static Border KeyCap(string text, double width) => new()
    {
        Width = width,
        Height = 32,
        CornerRadius = new CornerRadius(7),
        BorderBrush = Brush("NfKeyStrokeBrush"),
        BorderThickness = new Thickness(1),
        Background = new SolidColorBrush(ColorHelper.FromArgb(0x0A, 0xFF, 0xFF, 0xFF)),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 14,
            Foreground = Brush("NfTextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }
    };

    /// <summary>La flèche du pointeur, en trait blanc sur ombre.</summary>
    private static Path Cursor() => new()
    {
        Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), "M 0,0 L 0,15 L 4,11.5 L 6.8,17.5 L 9,16.6 L 6.3,10.8 L 11,10.8 Z"),
        Fill = new SolidColorBrush(Colors.White),
        Stroke = new SolidColorBrush(Colors.Black),
        StrokeThickness = 1
    };

    private static TextBlock Label(string text, double size, string brush) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = Brush(brush)
    };

    // ------------------------------------------------------------------
    // Mouvement
    // ------------------------------------------------------------------

    /// <summary>Boucle douce sur une propriété de composition ; rien sous réduction des animations.</summary>
    private static void Loop(UIElement element, string property, Vector3[] frames, double seconds, Vector3? center = null)
    {
        if (!MotionSettings.AnimationsEnabled)
        {
            return;
        }

        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        Compositor compositor = visual.Compositor;

        if (property == "Translation")
        {
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        }

        if (center is Vector3 point)
        {
            visual.CenterPoint = point;
        }

        CompositionEasingFunction standard = compositor.CreateCubicBezierEasingFunction(new Vector2(0.8f, 0f), new Vector2(0.2f, 1f));
        Vector3KeyFrameAnimation animation = compositor.CreateVector3KeyFrameAnimation();

        for (int i = 0; i < frames.Length; i++)
        {
            animation.InsertKeyFrame(i / (float)(frames.Length - 1), frames[i], standard);
        }

        animation.Duration = TimeSpan.FromSeconds(seconds);
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation(property, animation);
    }

    /// <summary>Changement de carte : illustration et texte glissent de 12 DIPs en fondu (200 ms).</summary>
    private void PlayStepEntrance()
    {
        if (!MotionSettings.AnimationsEnabled)
        {
            return;
        }

        foreach (UIElement element in new UIElement[] { IllustrationFrame, TextBlockHost })
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            Compositor compositor = visual.Compositor;
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);

            CompositionEasingFunction decelerate = compositor.CreateCubicBezierEasingFunction(new Vector2(0f, 0f), new Vector2(0f, 1f));

            ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0f, 0f);
            fade.InsertKeyFrame(1f, 1f, decelerate);
            fade.Duration = TimeSpan.FromMilliseconds(200);

            Vector3KeyFrameAnimation slide = compositor.CreateVector3KeyFrameAnimation();
            slide.InsertKeyFrame(0f, new Vector3(12, 0, 0));
            slide.InsertKeyFrame(1f, Vector3.Zero, decelerate);
            slide.Duration = TimeSpan.FromMilliseconds(200);

            visual.StartAnimation("Opacity", fade);
            visual.StartAnimation("Translation", slide);
        }
    }

    // ------------------------------------------------------------------

    private static Brush Brush(string key)
        => SpaceNotch_App.UI.ThemeBrushes.Get(key, new SolidColorBrush(Colors.White));

    private void Raise(string actionId)
    {
        if (_activityId is null)
        {
            return;
        }

        ActionRequested?.Invoke(this, new IslandActionRequest(_activityId, actionId));
    }
}
