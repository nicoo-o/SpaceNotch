using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpaceNotch.Core.Activities;
using SpaceNotch.Platform.Windows.Media;
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

    public MediaExpandedScene()
    {
        InitializeComponent();
    }

    public event EventHandler<IslandActionRequest>? ActionRequested;

    public FrameworkElement Root => this;

    public void Apply(IslandActivity activity)
    {
        _activityId = activity.Id;

        UpdateActions(activity.Actions);

        var track = activity.Payload as MediaTrackInfo;

        TitleText.Text = track?.Title ?? activity.Title;
        ArtistText.Text = track?.Artist ?? activity.Subtitle ?? string.Empty;

        bool playing = track?.IsPlaying ?? false;
        PlayPauseIcon.Glyph = playing ? "\uE769" : "\uE768";

        UpdateTimeline(track);
        _ = UpdateArtworkAsync(track);
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
