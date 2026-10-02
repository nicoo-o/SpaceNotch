using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch_App.Composition;
using SpaceNotch_App.Views;
using Windows.UI;

namespace SpaceNotch_App.Views.Scenes;

/// <summary>
/// Scène générique : icône, titre, sous-titre, et les contrôles que la
/// fonctionnalité déclare.
///
/// C'est la scène qui accueille le contenu d'un greffon tiers. Elle ne connaît
/// donc rien de ce qu'elle affiche : elle projette une activité et construit sa
/// rangée de boutons à partir des actions annoncées. Un greffon peut ainsi
/// proposer un contrôle sans qu'aucune ligne du rendu ne mentionne son domaine.
/// </summary>
public sealed partial class InfoScene : UserControl, IIslandSceneView
{
    /// <summary>Identifiant de l'activité présentée, transmis avec chaque action.</summary>
    private string? _activityId;

    public InfoScene()
    {
        InitializeComponent();
    }

    public event EventHandler<IslandActionRequest>? ActionRequested;

    /// <summary>Le pointeur entre sur un contrôle (<c>true</c>) ou en sort : le miroir de « Rejoindre » (W5).</summary>
    public event Action<string, string, bool>? ActionHovered;

    /// <summary>La vidéo du miroir, que la fenêtre relie à la caméra.</summary>
    public MediaPlayerElement Mirror => MirrorVideo;

    private DeliveryPayload? _delivery;

    public FrameworkElement Root => this;

    /// <summary>
    /// Le mouvement hypnotique est-il joué ? Renseigné par la fenêtre, qui seule
    /// connaît les préférences et la réduction des animations.
    /// </summary>
    public bool AnimateHypnotic { get; set; } = true;

    /// <summary>Rendu de Clawd choisi dans les réglages ; renseigné par la fenêtre.</summary>
    public ClawdStyle ClawdStyle { get; set; } = ClawdStyle.Faithful;

    private HypnoticSurface? _hypnotic;
    private byte[]? _artworkBytes;

    /// <summary>La pastille reçoit ce qui grandit depuis la forme compacte.</summary>
    public FrameworkElement? AnchorFor(MorphAnchorKind kind) => kind switch
    {
        MorphAnchorKind.Icon or MorphAnchorKind.Artwork => IconBadge,
        MorphAnchorKind.Title => TitleText,
        _ => null
    };

    /// <summary>
    /// Pastille : la grille d'un travail en cours d'abord — c'est l'information
    /// la plus vivante —, puis l'image, puis le glyphe.
    /// </summary>
    private void ApplyBadge(IslandActivity activity)
    {
        _hypnotic ??= HypnoticSurface.TryAttach(SceneHypnoticHost);

        // Claude Code : Clawd, sa mascotte, occupe toute la pastille.
        if (activity.Payload is ClawdPayload clawd)
        {
            _hypnotic?.SetPreset(HypnoticPreset.None, animate: false);
            SceneHypnoticHost.Visibility = Visibility.Collapsed;
            SceneIcon.Visibility = Visibility.Collapsed;
            SceneArtwork.Visibility = Visibility.Collapsed;

            SceneClawd.Pitch = 1.4;
            SceneClawd.PixelStyle = ClawdStyle;
            SceneClawd.Animate = AnimateHypnotic;
            SceneClawd.Mood = clawd.Mood;
            SceneClawd.Visibility = Visibility.Visible;
            return;
        }

        SceneClawd.Visibility = Visibility.Collapsed;

        HypnoticPreset preset = HypnoticField.Resolve(activity.MotionState, activity.MotionPreset);
        bool hypnotic = _hypnotic is not null && preset != HypnoticPreset.None;

        SceneHypnoticHost.Visibility = hypnotic ? Visibility.Visible : Visibility.Collapsed;
        _hypnotic?.SetPreset(hypnotic ? preset : HypnoticPreset.None, AnimateHypnotic);

        bool artwork = !hypnotic && activity.Artwork is { Length: > 0 };

        SceneIcon.Visibility = hypnotic || artwork ? Visibility.Collapsed : Visibility.Visible;
        SceneArtwork.Visibility = artwork ? Visibility.Visible : Visibility.Collapsed;

        if (artwork && !ReferenceEquals(activity.Artwork, _artworkBytes))
        {
            _artworkBytes = activity.Artwork;
            _ = LoadArtworkAsync(activity.Artwork);
        }
    }

    private async Task LoadArtworkAsync(byte[]? bytes)
    {
        try
        {
            SceneArtwork.Source = await ArtworkLoader.LoadAsync(bytes);
        }
        catch (Exception)
        {
            // Une image illisible laisse la pastille vide plutôt que la carte absente.
            SceneArtwork.Source = null;
        }
    }

    /// <summary>Arrête la grille quand la scène est masquée : rien ne tourne hors de la vue.</summary>
    public void Rest()
    {
        _hypnotic?.SetPreset(HypnoticPreset.None, animate: false);
        SceneClawd.Visibility = Visibility.Collapsed;
        MirrorPanel.Visibility = Visibility.Collapsed;
    }

    public void Apply(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        _activityId = activity.Id;

        TitleText.Text = activity.Title;
        SubtitleText.Text = activity.Subtitle ?? string.Empty;
        SceneIcon.Key = activity.IconKey;

        EyebrowText.Text = activity.Eyebrow ?? string.Empty;
        EyebrowText.Visibility = string.IsNullOrWhiteSpace(activity.Eyebrow) ? Visibility.Collapsed : Visibility.Visible;

        string? metric = activity.TrailingMetric;
        MetricText.Text = metric ?? string.Empty;
        MetricText.Visibility = metric is null ? Visibility.Collapsed : Visibility.Visible;

        bool steps = activity.Payload is ProgressStepsPayload { Segments.Count: > 1 };
        ProgressTrack.Visibility = activity.Progress is null || steps ? Visibility.Collapsed : Visibility.Visible;
        ProgressScale.ScaleX = Math.Clamp(activity.Progress ?? 0, 0, 1);
        ApplySteps(steps ? ((ProgressStepsPayload)activity.Payload!).Segments : null);

        ApplyBadge(activity);

        _delivery = activity.Payload as DeliveryPayload;
        DeliveryTrack.Visibility = _delivery is null ? Visibility.Collapsed : Visibility.Visible;
        LayoutDelivery();
        ApplyVoice(activity.Payload as VoicePayload);

        RebuildActions(activity.Actions);
    }

    /// <summary>Montre ou cache le miroir, avec l'état du micro et de la caméra.</summary>
    public void ShowMirror(bool visible, string? status)
    {
        MirrorPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        MirrorStatus.Text = status ?? string.Empty;
    }

    private void OnDeliveryTrackSizeChanged(object sender, SizeChangedEventArgs e) => LayoutDelivery();

    /// <summary>Trois points (préparation, en route, arrivé) et le véhicule à sa place.</summary>
    private void LayoutDelivery()
    {
        if (_delivery is not { } delivery || DeliveryTrack.ActualWidth <= 0)
        {
            return;
        }

        double width = DeliveryTrack.ActualWidth;
        double at = SpaceNotch.Core.Phone.Delivery.Position(delivery.Step, delivery.Eta, DateTimeOffset.Now, delivery.Since);
        DeliveryDone.Width = Math.Max(0, width * at);
        DeliveryCanvas.Children.Clear();

        for (int i = 0; i < 3; i++)
        {
            bool reached = (int)delivery.Step >= i;
            var dot = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = 6,
                Height = 6,
                RadiusX = 1,
                RadiusY = 1,
                Fill = Ink(reached ? "NfTextPrimaryBrush" : "NfStrokeStrongBrush", reached ? (byte)0xFF : (byte)0x50)
            };
            Canvas.SetLeft(dot, Math.Clamp((width * i / 2.0) - 3, 0, width - 6));
            Canvas.SetTop(dot, 24 - 4 - 4);
            DeliveryCanvas.Children.Add(dot);
        }

        var vehicle = new GlyphView
        {
            Key = SpaceNotch.Core.Phone.Delivery.VehicleGlyph(delivery.Kind),
            Size = 14,
            Tint = Ink("NfTextPrimaryBrush", 0xFF)
        };
        Canvas.SetLeft(vehicle, Math.Clamp((width * at) - 7, 0, width - 14));
        Canvas.SetTop(vehicle, 0);
        DeliveryCanvas.Children.Add(vehicle);
    }

    /// <summary>Une identicône par personne : pleine quand elle parle, estompée sinon, barrée de rouge si muette.</summary>
    private void ApplyVoice(VoicePayload? voice)
    {
        VoiceRow.Children.Clear();

        if (voice is null || voice.Members.Count == 0)
        {
            VoiceRow.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (SpaceNotch.Core.Social.VoiceMember member in voice.Members.Take(8))
        {
            var face = new IdenticonView { Width = 18, Height = 18 };
            face.Show(member.Id + member.Name, Ink("NfTextPrimaryBrush", 0xFF), animate: false);

            var ring = new Border
            {
                Padding = new Thickness(3),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1.5),
                BorderBrush = member.Speaking ? new SolidColorBrush(Color.FromArgb(0xFF, 0x5B, 0xE3, 0x8A)) : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                Opacity = member.Speaking ? 1 : member.Muted ? 0.3 : 0.55,
                Child = face
            };

            ToolTipService.SetToolTip(ring, member.Name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ring, member.Name);
            VoiceRow.Children.Add(ring);
        }

        if (voice.Members.Count > 8)
        {
            VoiceRow.Children.Add(new TextBlock
            {
                Text = "+" + (voice.Members.Count - 8).ToString(System.Globalization.CultureInfo.InvariantCulture),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Ink("NfTextSecondaryBrush", 0xA0)
            });
        }

        VoiceRow.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Barre à étapes (W1) : un segment par étape. Les segments sont recréés
    /// seulement quand leur nombre change ; sinon, seule l'échelle bouge.
    /// </summary>
    private void ApplySteps(IReadOnlyList<double>? segments)
    {
        if (segments is null)
        {
            StepsTrack.Visibility = Visibility.Collapsed;
            return;
        }

        if (StepsTrack.Children.Count != segments.Count)
        {
            StepsTrack.Children.Clear();
            StepsTrack.ColumnDefinitions.Clear();

            for (int i = 0; i < segments.Count; i++)
            {
                StepsTrack.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var fill = new Border
                {
                    CornerRadius = new CornerRadius(1.5),
                    Background = Ink("NfTextPrimaryBrush", 0xFF),
                    RenderTransformOrigin = new global::Windows.Foundation.Point(0, 0.5),
                    RenderTransform = new ScaleTransform { ScaleX = 0 }
                };

                var segment = new Grid { CornerRadius = new CornerRadius(1.5), Background = Ink("NfStrokeSubtleBrush", 0x24) };
                segment.Children.Add(fill);
                Grid.SetColumn(segment, i);
                StepsTrack.Children.Add(segment);
            }
        }

        for (int i = 0; i < segments.Count; i++)
        {
            if (StepsTrack.Children[i] is Grid { Children: [Border { RenderTransform: ScaleTransform scale }] })
            {
                scale.ScaleX = Math.Clamp(segments[i], 0, 1);
            }
        }

        StepsTrack.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Construit la rangée de contrôles à partir des actions déclarées.
    ///
    /// L'action principale est mise en avant — fond clair, glyphe sombre — parce
    /// qu'une carte qui propose deux contrôles doit indiquer lequel est le
    /// principal sans que la fonctionnalité ait à le décrire autrement.
    /// </summary>
    private void RebuildActions(IReadOnlyList<ActivityAction> actions)
    {
        ActionHost.Children.Clear();

        if (actions.Count == 0)
        {
            ActionHost.Visibility = Visibility.Collapsed;
            return;
        }

        double height = Token("NfActionHeight", 30.0);

        foreach (ActivityAction action in actions)
        {
            // Un contrôle se distingue par son **trait** et par son encre, jamais
            // par un fond plus clair : c'est la règle « sans hiérarchie » des
            // jetons de conception. La pastille blanche qui précédait était de
            // toute façon condamnée — sur un système en thème sombre, elle aurait
            // été le seul objet clair d'une carte noire.
            var button = new Button
            {
                Padding = Token("NfActionPadding", new Thickness(12, 0, 12, 0)),
                Height = height,
                CornerRadius = new CornerRadius(height / 2),
                BorderThickness = new Thickness(1),
                BorderBrush = Ink(action.IsPrimary ? "NfStrokeStrongBrush" : "NfStrokeSubtleBrush", 0x24),
                Background = new SolidColorBrush(Color.FromArgb(0x00, 0x00, 0x00, 0x00)),
                Foreground = Ink(action.IsPrimary ? "NfTextPrimaryBrush" : "NfTextSecondaryBrush", 0xC0),
                IsEnabled = action.IsEnabled,
                Content = BuildActionContent(action)
            };

            // L'identification passe par l'étiquette : la vue n'a aucune raison de
            // connaître la signification de l'identifiant qu'elle transporte.
            button.Tag = action.Id;
            button.Click += OnActionClicked;
            button.PointerEntered += (_, _) => { if (_activityId is { } id) { ActionHovered?.Invoke(id, action.Id, true); } };
            button.PointerExited += (_, _) => { if (_activityId is { } id) { ActionHovered?.Invoke(id, action.Id, false); } };

            ActionHost.Children.Add(button);
        }

        ActionHost.Visibility = Visibility.Visible;
    }

    private static StackPanel BuildActionContent(ActivityAction action)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (!string.IsNullOrEmpty(action.IconKey))
        {
            panel.Children.Add(new FontIcon
            {
                Glyph = GlyphCatalog.Resolve(action.IconKey),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = action.Label,
            FontSize = Token("NfActionFontSize", 11.5),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        return panel;
    }

    /// <summary>
    /// Lit un pinceau des jetons de conception.
    ///
    /// Une clé absente ne doit pas faire disparaître un contrôle : le repli est
    /// une teinte neutre, ce qui dégrade la finition mais laisse la carte
    /// utilisable. Une exception de ressource, elle, viderait la scène.
    /// </summary>
    private static Brush Ink(string key, byte fallbackAlpha)
        => Application.Current?.Resources?.TryGetValue(key, out object? value) == true && value is Brush brush
            ? brush
            : new SolidColorBrush(Color.FromArgb(fallbackAlpha, 0xFF, 0xFF, 0xFF));

    /// <summary>Lit une valeur des jetons, avec repli sur celle du code.</summary>
    private static double Token(string key, double fallback)
        => Application.Current?.Resources?.TryGetValue(key, out object? value) == true && value is double number
            ? number
            : fallback;

    private static Thickness Token(string key, Thickness fallback)
        => Application.Current?.Resources?.TryGetValue(key, out object? value) == true && value is Thickness thickness
            ? thickness
            : fallback;

    private void OnActionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string actionId } || _activityId is null)
        {
            return;
        }

        ActionRequested?.Invoke(this, new IslandActionRequest(_activityId, actionId));
    }

}
