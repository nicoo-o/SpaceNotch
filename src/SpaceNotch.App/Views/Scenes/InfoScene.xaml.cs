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
    private Color _deliveryTint;

    public FrameworkElement Root => this;

    // ---- Passage des yeux (vague 7) : où ils se posent dans la carte ouverte ----

    /// <summary>Le glyphe de la pastille.</summary>
    public GlyphView IconElement => SceneIcon;

    /// <summary>Clawd, quand c'est lui qui occupe la pastille.</summary>
    public ClawdView? ClawdElement => SceneClawd.Visibility == Visibility.Visible ? SceneClawd : null;

    /// <summary>Le titre.</summary>
    public TextBlock TitleElement => TitleText;

    /// <summary>Les contrôles, dans l'ordre.</summary>
    public IReadOnlyList<FrameworkElement> ActionElements
        => ActionHost.Visibility == Visibility.Visible ? ActionHost.Children.OfType<FrameworkElement>().ToList() : [];

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
        ProgressTrack.Visibility = activity.Progress is null || steps || activity.Layout == ActivityLayout.Row ? Visibility.Collapsed : Visibility.Visible;
        ProgressScale.ScaleX = Math.Clamp(activity.Progress ?? 0, 0, 1);
        ApplySteps(steps ? ((ProgressStepsPayload)activity.Payload!).Segments : null);

        ApplyBadge(activity);
        ApplyRecent(activity);

        _delivery = activity.Payload as DeliveryPayload;
        _deliveryTint = activity.Tint is { } t ? Color.FromArgb(0xFF, t.R, t.G, t.B) : Color.FromArgb(0xFF, 0xFF, 0xB2, 0x6B);
        DeliveryTrack.Visibility = _delivery is null ? Visibility.Collapsed : Visibility.Visible;
        LayoutDelivery();
        ApplyVoice(activity.Payload as VoicePayload);

        _row = activity.Layout == ActivityLayout.Row;
        _chips = activity.Layout != ActivityLayout.Card;
        ApplyLayout(activity);
        ApplyBadgeChip(activity.Badge);
        RebuildActions(activity.Actions, activity.ShowEnterHint);
    }

    // ---- Carte d'agent : compacte, développée d'un appui -------------------------

    /// <summary>Vrai quand un appui sur la carte la développe ou la replie.</summary>
    private bool _togglesDetails;

    /// <summary>Les dernières actions de l'agent, sous le texte, quand la carte est développée.</summary>
    private void ApplyRecent(IslandActivity activity)
    {
        var clawd = activity.Payload as ClawdPayload;
        _togglesDetails = clawd?.Recent is { Count: > 0 };
        DetailsHint.Visibility = _togglesDetails ? Visibility.Visible : Visibility.Collapsed;
        DetailsHint.Text = clawd?.Expanded == true ? "\u25B4" : "\u25BE";

        RecentList.Children.Clear();
        int shown = clawd?.ShownLines ?? 0;
        RecentList.Visibility = shown > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (shown == 0)
        {
            return;
        }

        Color tint = activity.Tint is { } t ? Color.FromArgb(0xFF, t.R, t.G, t.B) : Color.FromArgb(0xFF, 0xB3, 0x9D, 0xFF);
        IReadOnlyList<string> lines = clawd!.Recent!;

        for (int i = lines.Count - shown; i < lines.Count; i++)
        {
            // La plus récente est pleine, les précédentes s'estompent.
            bool latest = i == lines.Count - 1;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Height = 16 };
            row.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 5,
                Height = 5,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = new SolidColorBrush(latest ? tint : Color.FromArgb(0x66, tint.R, tint.G, tint.B))
            });
            row.Children.Add(new TextBlock
            {
                Text = lines[i],
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 250,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Ink(latest ? "NfTextSecondaryBrush" : "NfTextTertiaryBrush", 0xB0)
            });
            RecentList.Children.Add(row);
        }
    }

    /// <summary>
    /// Un appui sur une carte d'agent : la fonctionnalité la republie développée
    /// ou repliée. L'appui ne remonte pas à la fenêtre, qui refermerait la notch.
    /// Les boutons gardent leur propre clic : ils marquent l'appui comme traité.
    /// </summary>
    private void OnScenePointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_togglesDetails || _activityId is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Handled = true;
        ActionRequested?.Invoke(this, new IslandActionRequest(_activityId, ClawdPayload.ToggleAction));
    }

    // ---- Vague 6 : disposition en ligne, couleurs d'action, étiquettes ----------

    /// <summary>Vert plein (accepter) et rouge plein (refuser un appel) de la maquette.</summary>
    private static readonly Color Mint = Color.FromArgb(0xFF, 0x7F, 0xE8, 0xB0);
    private static readonly Color Coral = Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B);
    private static readonly Color OnColor = Color.FromArgb(0xFF, 0x04, 0x15, 0x0C);

    private bool _row;

    /// <summary>Pastilles (ligne et pile) plutôt que boutons au trait (carte).</summary>
    private bool _chips;

    private static readonly Color[] VoicePalette =
    [
        Color.FromArgb(0xFF, 0xFF, 0x8F, 0xA3),
        Color.FromArgb(0xFF, 0x7F, 0xE6, 0xFF),
        Color.FromArgb(0xFF, 0x7F, 0xE8, 0xB0),
        Color.FromArgb(0xFF, 0xFF, 0xB2, 0x6B),
        Color.FromArgb(0xFF, 0xB9, 0xA8, 0xFF)
    ];
    private Microsoft.UI.Composition.ScalarKeyFrameAnimation? _wiggle;

    /// <summary>
    /// Ligne : icône teintée sans cadre, contrôles et avatars à droite du texte.
    /// Carte : icône encadrée, contrôles en dessous. Les éléments changent de
    /// parent plutôt que d'être dupliqués : un seul jeu de contrôles existe.
    /// </summary>
    private void ApplyLayout(IslandActivity activity)
    {
        IconBadge.BorderThickness = new Thickness(_chips ? 0 : 1);
        IconBadge.Width = IconBadge.Height = _row ? 24 : 38;
        SceneIcon.Size = _chips ? 18 : 16;
        SceneIcon.Tint = _chips && activity.Tint is { } tint
            ? new SolidColorBrush(Color.FromArgb(0xFF, tint.R, tint.G, tint.B))
            : Ink("NfTextPrimaryBrush", 0xFF);

        Reparent(VoiceRow, _row ? TrailHost : SceneRoot, _row ? 1 : SceneRoot.Children.IndexOf(DeliveryTrack) + 1);
        Reparent(ActionHost, _row ? TrailHost : SceneRoot, _row ? TrailHost.Children.Count : SceneRoot.Children.IndexOf(MirrorPanel));
        ActionHost.HorizontalAlignment = _row ? HorizontalAlignment.Right : _chips ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        ActionHost.Margin = _chips && !_row ? new Thickness(50, 0, 0, 0) : new Thickness(0);
        ActionHost.Spacing = _chips ? 6 : 10;
        VoiceRow.Spacing = _row ? 4 : 8;

        // L'icône d'un appel qui sonne vibre, comme le téléphone.
        Wiggle(_row && activity.MotionState == ActivityMotionState.Attention && activity.IconKey == "Call");
    }

    private static void Reparent(FrameworkElement element, Panel target, int index)
    {
        if (element.Parent == target)
        {
            return;
        }

        if (element.Parent is Panel current)
        {
            current.Children.Remove(element);
        }

        target.Children.Insert(Math.Clamp(index, 0, target.Children.Count), element);
    }

    private void Wiggle(bool on)
    {
        Microsoft.UI.Composition.Visual visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(SceneIcon);

        if (!on || !GlyphView.AnimationsEnabled)
        {
            visual.StopAnimation("RotationAngleInDegrees");
            visual.RotationAngleInDegrees = 0;
            return;
        }

        visual.CenterPoint = new System.Numerics.Vector3((float)(SceneIcon.ActualWidth / 2), (float)(SceneIcon.ActualHeight / 2), 0);

        if (_wiggle is null)
        {
            _wiggle = visual.Compositor.CreateScalarKeyFrameAnimation();
            _wiggle.InsertKeyFrame(0.25f, -14f);
            _wiggle.InsertKeyFrame(0.5f, 0f);
            _wiggle.InsertKeyFrame(0.75f, 14f);
            _wiggle.InsertKeyFrame(1f, 0f);
            _wiggle.Duration = TimeSpan.FromMilliseconds(500);
            _wiggle.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
        }

        visual.StartAnimation("RotationAngleInDegrees", _wiggle);
    }

    /// <summary>« Copié », « Arrivée » : une étiquette pleine, devant le sous-titre ou à droite.</summary>
    private void ApplyBadgeChip(ActivityBadge? badge)
    {
        TrailBadge.Visibility = badge is { Inline: false } ? Visibility.Visible : Visibility.Collapsed;
        InlineBadge.Visibility = badge is { Inline: true } ? Visibility.Visible : Visibility.Collapsed;

        if (badge is null)
        {
            return;
        }

        Border chip = badge.Inline ? InlineBadge : TrailBadge;
        TextBlock text = badge.Inline ? InlineBadgeText : TrailBadgeText;
        chip.Background = new SolidColorBrush(ToneColor(badge.Tone) ?? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
        text.Foreground = new SolidColorBrush(badge.Tone == ActivityActionTone.Neutral ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF) : OnColor);
        text.Text = badge.Text;
    }

    private static Color? ToneColor(ActivityActionTone tone) => tone switch
    {
        ActivityActionTone.Positive => Mint,
        ActivityActionTone.Negative => Coral,
        _ => null
    };

    /// <summary>Montre ou cache le miroir rond, avec le micro et la caméra en usage.</summary>
    public void ShowMirror(bool visible, string? microphone, string? camera, bool microphoneMuted = false)
    {
        MirrorPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        MirrorMic.Text = microphone ?? string.Empty;
        MirrorCam.Text = camera ?? string.Empty;
        MirrorMicGlyph.Key = microphoneMuted ? "MicrophoneOff" : "Microphone";
        MirrorMicGlyph.Tint = new SolidColorBrush(microphoneMuted ? Coral : Mint);
        MirrorCamGlyph.Tint = new SolidColorBrush(Mint);
    }

    private void OnDeliveryTrackSizeChanged(object sender, SizeChangedEventArgs e) => LayoutDelivery();

    /// <summary>Trois points (préparation, en route, arrivé) et le véhicule à sa place.</summary>
    private void LayoutDelivery()
    {
        if (_delivery is not { } delivery || DeliveryTrack.ActualWidth <= 0)
        {
            return;
        }

        // La frise prend la couleur du service (orange Uber Eats…) ; l'arrivée passe au vert.
        Color tint = _deliveryTint;
        bool arrived = delivery.Step == SpaceNotch.Core.Phone.DeliveryStep.Arrived;
        double width = DeliveryTrack.ActualWidth;
        double at = SpaceNotch.Core.Phone.Delivery.Position(delivery.Step, delivery.Eta, DateTimeOffset.Now, delivery.Since);
        DeliveryDone.Width = Math.Max(0, width * at);
        DeliveryDone.Background = new SolidColorBrush(tint);
        DeliveryCanvas.Children.Clear();

        for (int i = 0; i < 3; i++)
        {
            bool reached = (int)delivery.Step >= i;
            var dot = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = 8,
                Height = 8,
                RadiusX = 2,
                RadiusY = 2,
                Fill = new SolidColorBrush(i == 2 && arrived ? Mint : reached ? tint : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF))
            };
            Canvas.SetLeft(dot, Math.Clamp((width * i / 2.0) - 4, 0, width - 8));
            Canvas.SetTop(dot, 4);
            DeliveryCanvas.Children.Add(dot);
        }

        // Le véhicule roule sur la ligne, entre deux étapes.
        if (!arrived)
        {
            var vehicle = new GlyphView
            {
                Key = SpaceNotch.Core.Phone.Delivery.VehicleGlyph(delivery.Kind),
                Size = 12,
                Tint = Ink("NfTextPrimaryBrush", 0xFF)
            };
            Canvas.SetLeft(vehicle, Math.Clamp((width * at) - 6, 0, width - 12));
            Canvas.SetTop(vehicle, -1);
            DeliveryCanvas.Children.Add(vehicle);
        }
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
            // Des avatars de pixels en couleur (maquette T3) ; celui qui parle s'éclaire.
            Color c = VoicePalette[Identicon.PaletteIndex(member.Id + member.Name, VoicePalette.Length)];
            var face = new IdenticonView { Width = 15, Height = 15 };
            face.Show(member.Id + member.Name, new SolidColorBrush(c), animate: false);

            var ring = new Border
            {
                Padding = new Thickness(2),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(member.Speaking ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0, 0, 0, 0)),
                Opacity = member.Muted && !member.Speaking ? 0.45 : 1,
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
    private void RebuildActions(IReadOnlyList<ActivityAction> actions, bool enterHint = false)
    {
        ActionHost.Children.Clear();

        if (actions.Count == 0)
        {
            ActionHost.Visibility = Visibility.Collapsed;
            return;
        }

        double height = _chips ? 26 : Token("NfActionHeight", 30.0);

        foreach (ActivityAction action in actions)
        {
            Color? fill = ToneColor(action.Tone);

            // En ligne, une bascule (le micro d'un salon) n'est qu'une icône :
            // rouge quand elle est coupée, comme dans la maquette.
            bool iconOnly = _row && action.Kind == ActivityActionKind.Toggle && fill is null;

            // Un contrôle ordinaire se distingue par son trait (carte) ou par une
            // pastille à peine plus claire (ligne) ; l'action qu'on attend, par sa couleur.
            var button = new Button
            {
                Padding = iconOnly ? new Thickness(4) : _chips ? new Thickness(11, 0, 11, 0) : Token("NfActionPadding", new Thickness(12, 0, 12, 0)),
                Height = height,
                MinWidth = iconOnly ? height : 0,
                CornerRadius = new CornerRadius(height / 2),
                BorderThickness = new Thickness(fill is null && !_chips ? 1 : 0),
                BorderBrush = Ink(action.IsPrimary ? "NfStrokeStrongBrush" : "NfStrokeSubtleBrush", 0x24),
                Background = fill is { } c
                    ? new SolidColorBrush(c)
                    : new SolidColorBrush(_chips && !iconOnly ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x00, 0x00, 0x00, 0x00)),
                Foreground = fill is not null
                    ? new SolidColorBrush(OnColor)
                    : iconOnly && action.IconKey == "MicrophoneOff"
                        ? new SolidColorBrush(Coral)
                        : Ink(action.IsPrimary || _chips ? "NfTextPrimaryBrush" : "NfTextSecondaryBrush", 0xC0),
                IsEnabled = action.IsEnabled,
                Content = iconOnly ? BuildIconOnly(action) : BuildActionContent(action, showIcon: !_chips)
            };

            ToolTipService.SetToolTip(button, action.Label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, action.Label);

            // L'identification passe par l'étiquette : la vue n'a aucune raison de
            // connaître la signification de l'identifiant qu'elle transporte.
            button.Tag = action.Id;
            button.Click += OnActionClicked;
            button.PointerEntered += (_, _) => { if (_activityId is { } id) { ActionHovered?.Invoke(id, action.Id, true); } };
            button.PointerExited += (_, _) => { if (_activityId is { } id) { ActionHovered?.Invoke(id, action.Id, false); } };

            ActionHost.Children.Add(button);
        }

        // « Entrée » : la touche lance l'action principale (OnIslandKeyDown).
        if (enterHint)
        {
            ActionHost.Children.Add(new Border
            {
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = SpaceNotch.Core.Localization.Lang.T("Entrée", "Enter"),
                    FontSize = 10.5,
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF))
                }
            });
        }

        ActionHost.Visibility = Visibility.Visible;
    }

    private static FontIcon BuildIconOnly(ActivityAction action) => new()
    {
        Glyph = GlyphCatalog.Resolve(action.IconKey),
        FontSize = 14,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static StackPanel BuildActionContent(ActivityAction action, bool showIcon = true)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (showIcon && !string.IsNullOrEmpty(action.IconKey))
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
