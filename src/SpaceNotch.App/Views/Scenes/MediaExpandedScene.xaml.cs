using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Platform.Windows.Media;
using SpaceNotch_App.Animations;
using SpaceNotch_App.Views;

namespace SpaceNotch_App.Views.Scenes;

/// <summary>
/// Scène média : pochette, progression déplaçable, contrôles de transport.
///
/// Les contrôles sont ceux que la fonctionnalité <em>déclare</em> : la vue
/// cherche les actions annoncées par identifiant et masque celles qui ne le sont
/// pas. Elle conserve une disposition dessinée plutôt qu'une liste générique —
/// une piste musicale mérite une mise en page, pas une barre de boutons — mais
/// elle n'invente aucun contrôle et n'exécute aucune action elle-même.
/// </summary>
public sealed partial class MediaExpandedScene : UserControl, IIslandSceneView
{
    public const string PreviousAction = "media.previous";

    public const string PlayPauseAction = "media.playpause";

    public const string NextAction = "media.next";

    public const string SeekAction = "media.seek";

    /// <summary>
    /// Vrai pendant une mise à jour programmée de la timeline. Sans ce verrou, la
    /// valeur réinjectée par la fonctionnalité déclencherait un déplacement
    /// renvoyé vers elle, et la tête de lecture sauterait en boucle sur elle-même.
    /// </summary>
    private bool _updatingTimeline;

    private string? _activityId;
    private byte[]? _artworkBytes;

    // Paroles (T4) : la position est estimée entre deux nouvelles de Windows,
    // qui n'en donne qu'au changement d'état ; une horloge à 4 Hz suffit.
    private SpaceNotch.Core.Media.SyncedLyrics? _lyrics;
    private TimeSpan _position;
    private DateTime _positionAt = DateTime.UtcNow;
    private bool _playing;
    private DispatcherTimer? _lyricsClock;
    private bool? _liked;

    public MediaExpandedScene()
    {
        InitializeComponent();
        Unloaded += (_, _) => _lyricsClock?.Stop();
    }

    /// <summary>« J'aime » touché : la fenêtre le transmet à Spotify.</summary>
    public event Action<bool>? LikeRequested;

    /// <summary>« file » touché : la fenêtre ajoute le morceau à la file Spotify.</summary>
    public event Action? QueueRequested;

    // Le mode paroles est un choix de l'utilisateur, gardé d'un morceau à l'autre.
    private static bool s_preferLyrics = true;

    private static readonly SolidColorBrush SungBrush = new(global::Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush NextBrush = new(global::Windows.UI.Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush FarBrush = new(global::Windows.UI.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush LikedBrush = new(global::Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x8F, 0xA3));
    private static readonly SolidColorBrush UnlikedBrush = new(global::Windows.UI.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush QueuedBrush = new(global::Windows.UI.Color.FromArgb(0xFF, 0x7F, 0xE8, 0xB0));
    private static readonly SolidColorBrush QueueBrush = new(global::Windows.UI.Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));

    /// <summary>Hauteur d'une ligne de paroles et l'espace qui la suit : le pas du défilement.</summary>
    private const double LyricPitch = 26 + 4;

    // Les lignes affichées (les vides sont sautées) et leur rang dans les paroles.
    private readonly System.Collections.Generic.List<(int Index, TextBlock Text)> _lyricLines = [];
    private int _sung = int.MinValue;
    private Microsoft.UI.Xaml.Media.Animation.Storyboard? _lyricsScroll;
    private DispatcherTimer? _queuedReset;

    /// <summary>Les paroles du morceau, ou <c>null</c> : la scène rend les contrôles.</summary>
    public void SetLyrics(SpaceNotch.Core.Media.SyncedLyrics? lyrics)
    {
        _lyrics = lyrics;
        _sung = int.MinValue;
        _lyricLines.Clear();
        LyricsList.Children.Clear();
        LyricsShift.Y = 0;

        if (lyrics is not null)
        {
            for (int i = 0; i < lyrics.Lines.Count; i++)
            {
                if (lyrics.Lines[i].Text.Length == 0)
                {
                    continue;
                }

                var text = new TextBlock
                {
                    Text = lyrics.Lines[i].Text,
                    FontSize = 17,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Height = 26,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    Foreground = FarBrush
                };
                _lyricLines.Add((i, text));
                LyricsList.Children.Add(text);
            }
        }

        UpdateLyric();
    }

    /// <summary>« Ensuite : … », ajouté à la ligne de l'artiste ; rien pour l'effacer.</summary>
    public void SetNext(string? line)
    {
        _next = line;
        ApplyArtistLine();
    }

    private string? _next;
    private string _artist = string.Empty;

    /// <summary>L'artiste, et ce qui vient ensuite.</summary>
    private void ApplyArtistLine()
        => ArtistText.Text = string.IsNullOrEmpty(_next) ? _artist : _artist + "  ·  " + _next;

    /// <summary>Le cœur : <c>null</c> le cache (Spotify non connecté ou morceau introuvable).</summary>
    public void SetLiked(bool? liked)
    {
        _liked = liked;
        LikeButton.Visibility = liked is null ? Visibility.Collapsed : Visibility.Visible;
        LikeIcon.Tint = liked == true
            ? new SolidColorBrush(global::Windows.UI.Color.FromArgb(0xFF, 0x1E, 0xD7, 0x60))
            : (Brush)Application.Current.Resources["NfTextSecondaryBrush"];
        LyricsLikeIcon.Tint = liked == true ? LikedBrush : UnlikedBrush;
        LyricsSide.Visibility = liked is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Le morceau est parti dans la file (ou non) : « file » le dit deux secondes.</summary>
    public void ShowQueued(bool added)
    {
        QueueText.Text = added ? Lang.T("ajouté", "added") : Lang.T("échec", "failed");
        QueueText.Foreground = added ? QueuedBrush : new SolidColorBrush(global::Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B));
        _queuedReset ??= CreateQueuedReset();
        _queuedReset.Stop();
        _queuedReset.Start();
    }

    private DispatcherTimer CreateQueuedReset()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            QueueText.Text = Lang.T("file", "queue");
            QueueText.Foreground = QueueBrush;
        };
        return timer;
    }

    private void OnLikeClicked(object sender, RoutedEventArgs e)
    {
        bool next = _liked != true;
        SetLiked(next);
        LikeRequested?.Invoke(next);
    }

    private void OnQueueClicked(object sender, RoutedEventArgs e) => QueueRequested?.Invoke();

    private void OnLyricsHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        LyricsList.Width = e.NewSize.Width;
        LyricsClipHost.Clip = new RectangleGeometry { Rect = new global::Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
    }

    private void OnLyricsViewportClicked(object sender, RoutedEventArgs e)
    {
        s_preferLyrics = false;
        UpdateLyric();
        PlayPauseButton.Focus(FocusState.Programmatic);
    }

    private void OnLyricsClicked(object sender, RoutedEventArgs e)
    {
        s_preferLyrics = true;
        UpdateLyric();
        LyricsViewport.Focus(FocusState.Programmatic);
    }

    /// <summary>Paroles ou contrôles : un seul des deux à la fois, pour que la scène garde sa taille.</summary>
    private void ApplyMode()
    {
        bool lyrics = _lyricLines.Count > 0 && s_preferLyrics;
        LyricsPanel.Visibility = lyrics ? Visibility.Visible : Visibility.Collapsed;
        ControlsPanel.Visibility = lyrics ? Visibility.Collapsed : Visibility.Visible;
        LyricsButton.Visibility = _lyricLines.Count > 0 && !lyrics ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateLyric()
    {
        ApplyMode();

        if (_lyrics is null || _lyricLines.Count == 0 || LyricsPanel.Visibility != Visibility.Visible)
        {
            _lyricsClock?.Stop();
            return;
        }

        TimeSpan now = _playing ? _position + (DateTime.UtcNow - _positionAt) : _position;
        int index = _lyrics.IndexAt(now);

        // La ligne affichée chantée : la dernière dont le rang est atteint (-1 avant la première).
        int sung = -1;

        for (int i = 0; i < _lyricLines.Count && _lyricLines[i].Index <= index; i++)
        {
            sung = i;
        }

        if (sung != _sung)
        {
            bool first = _sung == int.MinValue;
            _sung = sung;

            for (int i = 0; i < _lyricLines.Count; i++)
            {
                _lyricLines[i].Text.Foreground = i == sung ? SungBrush : i == sung + 1 ? NextBrush : FarBrush;
            }

            ScrollLyrics(-Math.Max(sung, 0) * LyricPitch, animate: !first);
        }

        if (_playing && IsLoaded)
        {
            _lyricsClock ??= CreateLyricsClock();
            _lyricsClock.Start();
        }
        else
        {
            _lyricsClock?.Stop();
        }
    }

    /// <summary>Les lignes remontent d'un cran, avec un léger rebond.</summary>
    private void ScrollLyrics(double to, bool animate)
    {
        _lyricsScroll?.Stop();

        if (!animate || !GlyphView.AnimationsEnabled)
        {
            LyricsShift.Y = to;
            return;
        }

        var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = LyricsShift.Y,
            To = to,
            Duration = TimeSpan.FromMilliseconds(450),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.BackEase
            {
                Amplitude = 0.25,
                EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut
            }
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, LyricsShift);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Y");
        _lyricsScroll = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        _lyricsScroll.Children.Add(animation);
        _lyricsScroll.Completed += (_, _) => LyricsShift.Y = to;
        _lyricsScroll.Begin();
    }

    private DispatcherTimer CreateLyricsClock()
    {
        var clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        clock.Tick += (_, _) =>
        {
            if (Visibility != Visibility.Visible)
            {
                clock.Stop();
                return;
            }

            UpdateLyric();
        };
        return clock;
    }

    /// <summary>Inclinaison maximale de la pochette, en degrés.</summary>
    private const double MaxTilt = 8;

    /// <summary>
    /// Pochette en 3D (S2) : sous le pointeur, elle s'incline vers lui et un
    /// reflet glisse dessus comme sur du papier glacé.
    /// </summary>
    private void OnArtworkPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!GlyphView.AnimationsEnabled || ArtworkBorder.ActualWidth <= 0)
        {
            return;
        }

        global::Windows.Foundation.Point p = e.GetCurrentPoint(ArtworkBorder).Position;
        double nx = Math.Clamp(p.X / ArtworkBorder.ActualWidth, 0, 1) - 0.5;
        double ny = Math.Clamp(p.Y / ArtworkBorder.ActualHeight, 0, 1) - 0.5;

        _tiltBack?.Stop();
        ArtworkTilt.RotationY = -nx * 2 * MaxTilt;
        ArtworkTilt.RotationX = ny * 2 * MaxTilt;
        ArtworkShineBrush.Center = new global::Windows.Foundation.Point(nx + 0.5, ny + 0.5);
        ArtworkShineBrush.GradientOrigin = ArtworkShineBrush.Center;
        ArtworkShine.Opacity = 1;
    }

    private Microsoft.UI.Xaml.Media.Animation.Storyboard? _tiltBack;

    /// <summary>Le pointeur part : la pochette revient à plat, sur un ressort.</summary>
    private void OnArtworkPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var ease = new Microsoft.UI.Xaml.Media.Animation.ElasticEase { Oscillations = 1, Springiness = 6, EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut };
        _tiltBack = new Microsoft.UI.Xaml.Media.Animation.Storyboard();

        foreach ((string property, double from) in new[] { ("RotationX", ArtworkTilt.RotationX), ("RotationY", ArtworkTilt.RotationY) })
        {
            var back = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = from,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(GlyphView.AnimationsEnabled ? 420 : 1)),
                EasingFunction = ease,
                EnableDependentAnimation = true
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(back, ArtworkTilt);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(back, property);
            _tiltBack.Children.Add(back);
        }

        _tiltBack.Begin();
        ArtworkShine.Opacity = 0;
    }

    public event EventHandler<IslandActionRequest>? ActionRequested;

    public FrameworkElement Root => this;

    /// <summary>
    /// La pochette grandit depuis la pochette compacte ; le titre se déplace
    /// depuis le libellé de la notch.
    /// </summary>
    public FrameworkElement? AnchorFor(MorphAnchorKind kind) => kind switch
    {
        MorphAnchorKind.Artwork or MorphAnchorKind.Icon => ArtworkBorder,
        MorphAnchorKind.Title => TitleText,
        MorphAnchorKind.Subtitle => ArtistText,
        _ => null
    };

    public void Apply(IslandActivity activity)
    {
        _activityId = activity.Id;

        UpdateActions(activity.Actions);

        var track = activity.Payload as MediaTrackInfo;

        ScrambleText.Set(TitleText, track?.Title ?? activity.Title, GlyphView.AnimationsEnabled && IsLoaded);
        _artist = track?.Artist ?? activity.Subtitle ?? string.Empty;
        ApplyArtistLine();

        bool playing = track?.IsPlaying ?? false;
        PlayPauseIcon.Key = playing ? "Pause" : "Play";

        UpdateTimeline(track);
        _ = UpdateArtworkAsync(track);

        // Une nouvelle position de Windows recale l'horloge des paroles.
        TimeSpan position = track?.Position ?? TimeSpan.Zero;

        if (position != _position || playing != _playing)
        {
            _position = position;
            _positionAt = DateTime.UtcNow;
        }

        _playing = playing;
        UpdateLyric();
    }

    /// <summary>
    /// Active les contrôles annoncés par la fonctionnalité. L'implémentation par
    /// identifiant — et non par type de contenu — est ce qui permettra à une autre
    /// source média de n'exposer que deux contrôles sans que la vue change.
    /// </summary>
    private void UpdateActions(System.Collections.Generic.IReadOnlyList<ActivityAction> actions)
    {
        PrevButton.Visibility = actions.Any(a => a.Id == PreviousAction)
            ? Visibility.Visible
            : Visibility.Collapsed;

        NextButton.Visibility = actions.Any(a => a.Id == NextAction)
            ? Visibility.Visible
            : Visibility.Collapsed;

        ActivityAction? primary = actions.FirstOrDefault(a => a.Kind == ActivityActionKind.Toggle);

        PlayPauseButton.IsEnabled = primary?.IsEnabled ?? false;
        PlayPauseButton.Visibility = primary is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateTimeline(MediaTrackInfo? track)
    {
        double duration = track?.Duration.TotalSeconds ?? 0;
        double position = track?.Position.TotalSeconds ?? 0;

        _updatingTimeline = true;

        try
        {
            TimelineSlider.Maximum = duration > 0 ? duration : 100;
            TimelineSlider.IsEnabled = duration > 0;
            TimelineSlider.Value = duration > 0 ? Math.Clamp(position, 0, duration) : 0;
        }
        finally
        {
            _updatingTimeline = false;
        }

        TimeText.Text = $"{Format(track?.Position ?? TimeSpan.Zero)} / {Format(track?.Duration ?? TimeSpan.Zero)}";

        // Sans durée, la frise n'aurait rien à dire : « 0:00 / 0:00 » laissait
        // croire à une lecture arrêtée. À la place, ce qui est vrai.
        bool known = duration > 0;
        TimelineRow.Visibility = known ? Visibility.Visible : Visibility.Collapsed;
        LiveRow.Visibility = known ? Visibility.Collapsed : Visibility.Visible;

        if (!known)
        {
            bool playing = track?.IsPlaying ?? false;
            string state = playing ? Lang.T("En lecture", "Playing") : Lang.T("En pause", "Paused");
            string source = MediaSource.FriendlyName(track?.AppId);

            LiveText.Text = source.Length == 0 ? state : $"{state} · {source}";
            LiveBars.Tint = (Brush)Application.Current.Resources["NfTextSecondaryBrush"];
            LiveBars.Show(playing ? new CompactTrailing(TrailingKind.Equalizer, 0) : CompactTrailing.None);
        }
    }

    private async Task UpdateArtworkAsync(MediaTrackInfo? track)
    {
        byte[]? bytes = track?.ArtworkBytes;

        if (ReferenceEquals(bytes, _artworkBytes) && bytes is not null)
        {
            return;
        }

        _artworkBytes = bytes;

        var source = await ArtworkLoader.LoadAsync(bytes);

        ArtworkImage.Source = source;

        ArtworkImage.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
        ArtworkFallback.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTimelineValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_updatingTimeline)
        {
            return;
        }

        // La valeur est transmise sous forme de texte : le cœur et la vue restent
        // sans dépendance à un type de média particulier.
        Raise(SeekAction, e.NewValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private void OnPlayPauseClicked(object sender, RoutedEventArgs e) => Raise(PlayPauseAction);

    private void OnPrevClicked(object sender, RoutedEventArgs e) => Raise(PreviousAction);

    private void OnNextClicked(object sender, RoutedEventArgs e) => Raise(NextAction);

    private void Raise(string actionId, string? value = null)
    {
        if (_activityId is null)
        {
            return;
        }

        ActionRequested?.Invoke(this, new IslandActionRequest(_activityId, actionId, value));
    }

    private static string Format(TimeSpan value)
        => value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString(@"m\:ss", System.Globalization.CultureInfo.InvariantCulture);
}
