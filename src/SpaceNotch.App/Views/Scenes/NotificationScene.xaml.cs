using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Presentation;
using SpaceNotch_App.Animations;
using SpaceNotch_App.Views;

namespace SpaceNotch_App.Views.Scenes;

/// <summary>
/// Groupe de notifications d'une application, ouvert.
/// </summary>
public sealed partial class NotificationScene : UserControl, IIslandSceneView
{
    /// <summary>Messages précédents montrés sous le plus récent.</summary>
    private const int HistoryLines = 3;

    public event Action? DismissRequested;

    public NotificationScene()
    {
        InitializeComponent();
    }

    public FrameworkElement Root => this;

    /// <summary>Le glyphe et le titre prolongent ceux de la forme compacte.</summary>
    public FrameworkElement? AnchorFor(MorphAnchorKind kind) => kind switch
    {
        MorphAnchorKind.Icon or MorphAnchorKind.Artwork => AppIcon,
        MorphAnchorKind.Title => TitleText,
        _ => null
    };

    public void Apply(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        ApplyLogo(activity.Artwork);

        if (activity.Payload is NotificationGroupPayload group && group.Count > 0)
        {
            AppSourceText.Text = group.AppName;
            CountText.Text = group.Count > 1
                ? group.Count.ToString(System.Globalization.CultureInfo.CurrentCulture)
                : string.Empty;
            CountText.Visibility = group.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

            ScrambleText.Set(TitleText, group.Items[0].Title, GlyphView.AnimationsEnabled && IsLoaded);
            BodyText.Text = group.Items[0].Body;

            RebuildHistory(group);
            return;
        }

        // Une notification sans groupe — un greffon, par exemple — se lit quand
        // même : titre et sous-titre de l'activité.
        AppSourceText.Text = activity.Source ?? activity.Eyebrow ?? string.Empty;
        CountText.Visibility = Visibility.Collapsed;
        TitleText.Text = activity.Title;
        BodyText.Text = activity.Subtitle ?? string.Empty;
        HistoryHost.Children.Clear();
    }

    /// <summary>Largeur d'une carte de l'éventail, en DIPs.</summary>
    private const double CardWidth = 150;

    private readonly List<Border> _cards = [];

    /// <summary>
    /// Les messages précédents deviennent une pile de cartes : empilées, elles
    /// dépassent de quelques DIPs ; au survol, elles s'ouvrent en éventail (S1).
    /// </summary>
    private void RebuildHistory(NotificationGroupPayload group)
    {
        HistoryHost.Children.Clear();
        _cards.Clear();

        // La plus ancienne dessous, la plus récente dessus : on lit de gauche à droite.
        List<NotificationItem> items = group.Items.Skip(1).Take(HistoryLines).ToList();
        HistoryHost.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        for (int i = items.Count - 1; i >= 0; i--)
        {
            NotificationItem item = items[i];
            var text = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = item.Title, Style = AppSourceText.Style, Foreground = TitleText.Foreground, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = item.Body, Style = BodyText.Style, Foreground = AppSourceText.Foreground, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis });

            var card = new Border
            {
                Width = CardWidth,
                Height = CardFan.CardHeight,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 3, 10, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = CloseButton.Background,
                Child = text
            };

            HistoryHost.Children.Add(card);
            _cards.Insert(0, card);
        }

        Fan(open: false, animate: false);
    }

    private void OnHistoryPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => Fan(open: true, animate: true);

    private void OnHistoryPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => Fan(open: false, animate: true);

    /// <summary>Place les cartes, ouvertes ou empilées, sur un ressort un peu rebondissant.</summary>
    private void Fan(bool open, bool animate)
    {
        IReadOnlyList<FanSlot> slots = CardFan.Layout(_cards.Count, open);

        for (int i = 0; i < _cards.Count && i < slots.Count; i++)
        {
            FanSlot slot = slots[i];
            Border card = _cards[i];
            Microsoft.UI.Composition.Visual visual = ElementCompositionPreview.GetElementVisual(card);
            ElementCompositionPreview.SetIsTranslationEnabled(card, true);
            visual.CenterPoint = new Vector3((float)(CardWidth / 2), (float)(CardFan.CardHeight / 2), 0);

            var translation = new Vector3((float)slot.Offset, 0, 0);
            var scale = new Vector3((float)slot.Scale, (float)slot.Scale, 1);
            float angle = (float)slot.Rotation;

            if (!animate || !GlyphView.AnimationsEnabled)
            {
                visual.Properties.InsertVector3("Translation", translation);
                visual.Scale = scale;
                visual.RotationAngleInDegrees = angle;
                continue;
            }

            Microsoft.UI.Composition.Compositor compositor = visual.Compositor;
            Microsoft.UI.Composition.SpringVector3NaturalMotionAnimation move = compositor.CreateSpringVector3Animation();
            move.FinalValue = translation;
            move.DampingRatio = 0.7f;
            move.Period = TimeSpan.FromMilliseconds(55);
            visual.StartAnimation("Translation", move);

            Microsoft.UI.Composition.SpringVector3NaturalMotionAnimation grow = compositor.CreateSpringVector3Animation();
            grow.FinalValue = scale;
            grow.DampingRatio = 0.7f;
            grow.Period = TimeSpan.FromMilliseconds(55);
            visual.StartAnimation("Scale", grow);

            Microsoft.UI.Composition.SpringScalarNaturalMotionAnimation tilt = compositor.CreateSpringScalarAnimation();
            tilt.FinalValue = angle;
            tilt.DampingRatio = 0.7f;
            tilt.Period = TimeSpan.FromMilliseconds(55);
            visual.StartAnimation("RotationAngleInDegrees", tilt);
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        DismissRequested?.Invoke();
    }

    private byte[]? _logoBytes;

    /// <summary>Le vrai logo de l'application, ou la cloche en pixels.</summary>
    private void ApplyLogo(byte[]? bytes)
    {
        bool logo = bytes is { Length: > 0 };
        AppLogo.Visibility = logo ? Visibility.Visible : Visibility.Collapsed;
        AppGlyph.Visibility = logo ? Visibility.Collapsed : Visibility.Visible;

        if (logo && !ReferenceEquals(bytes, _logoBytes))
        {
            _logoBytes = bytes;
            _ = LoadLogoAsync(bytes);
        }
    }

    private async System.Threading.Tasks.Task LoadLogoAsync(byte[]? bytes)
    {
        try
        {
            AppLogoImage.Source = await ArtworkLoader.LoadAsync(bytes);
        }
        catch (Exception)
        {
            AppLogo.Visibility = Visibility.Collapsed;
            AppGlyph.Visibility = Visibility.Visible;
        }
    }
}
