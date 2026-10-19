using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Menu;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Features.Menu;
using SpaceNotch_App.UI;
using Windows.System;

namespace SpaceNotch_App.Views.Scenes;

/// <summary>
/// Menu rapide : Rechercher, Minuteur (5 / 15 min et Focus 25 min au survol),
/// Presse-papier, Étagère · Détacher, Accrocher à… (trois bords dépliés sur
/// place) · Réglages, Quitter. La vue décrit ; la fenêtre exécute.
/// </summary>
public sealed partial class QuickMenuScene : UserControl, IIslandSceneView, ILoopingView
{
    private static readonly bool French = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";

    /// <summary>Durées simples ; la troisième puce est « Focus 25 min » (n° 75 : Pomodoro fondu dans le Minuteur).</summary>
    private static readonly int[] TimerChoices = [5, 15];

    /// <summary>Valeur de la puce Focus, et de l'arrêt d'un focus en cours.</summary>
    public const string FocusValue = "focus";

    public const string FocusStopValue = "focus.stop";

    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(15) };

    private string? _activityId;
    private QuickMenuPayload? _shown;
    private bool _cascadePending = true;

    /// <summary>
    /// Les lignes déjà construites, par place, avec ce qui les a construites :
    /// une ligne inchangée est gardée telle quelle (n° 46b). Les recréer toutes
    /// à chaque ouverture coûtait 25 à 36 ms de rendu (visite du 2026-10-19).
    /// </summary>
    private readonly Dictionary<string, (string Key, UIElement Element)> _built = new(StringComparer.Ordinal);

    /// <summary>Un pinceau du thème lu à la dernière construction : un autre thème refait toutes les lignes.</summary>
    private Brush? _look;

    public QuickMenuScene()
    {
        InitializeComponent();

        BuildLogo();
        _clock.OnTick((_, _) => UpdateClock());

        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) =>
        {
            if (Visibility == Visibility.Visible)
            {
                _cascadePending = true;
                _shown = null;
                UpdateClock();
                _clock.Start();
            }
            else
            {
                _clock.Stop();

                // Les lignes gardées ne doivent pas rouvrir survolées ni appuyées
                // (le clic qui a refermé le menu), ni avec les puces du Minuteur dépliées.
                _hideTimerChips?.Invoke();
                ResetPointerStates(RowsPanel);
            }
        });
    }

    /// <summary>Replie les puces du Minuteur gardé.</summary>
    private Action? _hideTimerChips;

    private static void ResetPointerStates(UIElement element)
    {
        if (element is Control control)
        {
            VisualStateManager.GoToState(control, control.IsEnabled ? "Normal" : "Disabled", false);
        }

        if (element is Panel panel)
        {
            foreach (UIElement child in panel.Children)
            {
                ResetPointerStates(child);
            }
        }
    }

    public event EventHandler<IslandActionRequest>? ActionRequested;

    /// <inheritdoc />
    /// <remarks>L'heure du menu se relit toutes les 15 s : pas pendant le retrait (n° 49).</remarks>
    public void SetLoopsShown(bool shown)
    {
        if (!shown)
        {
            _clock.Stop();
        }
        else if (Visibility == Visibility.Visible)
        {
            UpdateClock();
            _clock.Start();
        }
    }

    public FrameworkElement Root => this;

    public void Apply(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        _activityId = activity.Id;

        if (activity.Payload is not QuickMenuPayload payload || payload == _shown)
        {
            return;
        }

        bool dockToggled = _shown is not null && _shown.DockExpanded != payload.DockExpanded;
        _shown = payload;

        Build(payload);

        if (dockToggled)
        {
            FocusRow(QuickMenuFeature.DockExpandAction);
        }
    }

    /// <summary>Premier élément sous le clavier, sans anneau : le menu s'ouvre au clic droit.</summary>
    public void FocusFirst()
    {
        if (RowsPanel.Children.Count > 0 && FindFirstButton(RowsPanel.Children[0]) is Button first)
        {
            first.Focus(FocusState.Pointer);
        }
    }

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    private void Build(QuickMenuPayload payload)
    {
        // Un autre thème (clair, sombre, contraste élevé) : les lignes gardées ont les pinceaux de l'ancien.
        Brush look = Brush("NfTextSecondaryBrush");

        if (!ReferenceEquals(look, _look))
        {
            _look = look;
            _built.Clear();
        }

        var rows = new List<UIElement>(14);

        rows.Add(Kept("search", payload.Hotkey ?? string.Empty, () => Row(
            "Search",
            French ? "Rechercher" : "Search",
            QuickMenuFeature.SearchAction,
            keys: payload.Hotkey?.Replace("Win+", "⊞+", StringComparison.Ordinal).Split('+'))));

        rows.Add(Kept("timer", $"{payload.TimerRunning}|{payload.FocusActive}", () => TimerRow(payload.TimerRunning, payload.FocusActive)));

        // Un focus (en cours ou en pause) a sa propre ligne d'arrêt : le Minuteur
        // garde la sienne, ou ses puces pour un thé pendant le focus.
        if (payload.FocusActive)
        {
            rows.Add(Kept("focus.stop", string.Empty, () => Row("Stop", French ? "Arrêter le focus" : "Stop focus", QuickMenuFeature.TimerAction, value: FocusStopValue)));
        }

        // Note éclair (F7) : aussi par un double-clic sur la notch. Nom et icône
        // des lignes communes viennent des tuiles : une seule source (n° 50).
        QuickMenuTile note = QuickMenuTiles.For(QuickMenuFeature.NoteAction)!;
        rows.Add(Kept("note", $"{note.IconKey}|{note.Label}", () => Row(note.IconKey, note.Label, QuickMenuFeature.NoteAction)));

        QuickMenuTile clipboard = QuickMenuTiles.For(QuickMenuFeature.ClipboardAction)!;
        rows.Add(Kept("clipboard", $"{clipboard.IconKey}|{clipboard.Label}|{payload.HasClipboard}|{payload.ClipboardOff}", () => Row(
            clipboard.IconKey,
            clipboard.Label,
            QuickMenuFeature.ClipboardAction,
            // Éteint, l'historique n'est pas « vide » : la ligne propose de l'activer (n° 63).
            enabled: payload.HasClipboard || payload.ClipboardOff,
            trailing: payload.HasClipboard ? null : payload.ClipboardOff ? (French ? "désactivé" : "off") : (French ? "vide" : "empty"))));

        rows.Add(Kept("shelf", $"{payload.HasShelf}", () => Row(
            "Folder",
            French ? "Étagère" : "Shelf",
            QuickMenuFeature.ShelfAction,
            enabled: payload.HasShelf,
            trailing: payload.HasShelf ? null : (French ? "vide" : "empty"))));

        rows.Add(Kept("separator.1", string.Empty, Separator));

        rows.Add(Kept("detach", payload.IsFloating ? $"floating|{payload.Edge}" : "docked", () => payload.IsFloating
            ? Row("Pin", French ? "Raccrocher au bord" : "Reattach to edge", QuickMenuFeature.DockAction, value: payload.Edge.ToString())
            : Row("Detach", French ? "Détacher de l'écran" : "Detach from screen", QuickMenuFeature.DetachAction)));

        rows.Add(Kept("dock", $"{payload.DockExpanded}", () => Row(
            "Dock",
            French ? "Accrocher à…" : "Dock to…",
            QuickMenuFeature.DockExpandAction,
            value: payload.DockExpanded ? "0" : "1",
            chevron: payload.DockExpanded ? "ChevronDown" : "ChevronRight")));

        // Les bords se déplient en glissant : refaits à chaque fois.
        if (payload.DockExpanded)
        {
            rows.Add(DockChoices(payload));
        }

        rows.Add(Kept("separator.2", string.Empty, Separator));

        rows.Add(Kept("settings", string.Empty, () => Row("Settings", French ? "Réglages" : "Settings", QuickMenuFeature.SettingsAction, keys: ["Ctrl", ","])));
        rows.Add(Kept("quit", string.Empty, () => Row("Power", French ? "Quitter" : "Quit", QuickMenuFeature.QuitAction)));

        Place(rows);

        if (_cascadePending)
        {
            _cascadePending = false;
            PlayCascade();
        }
    }

    /// <summary>
    /// La ligne de la place <paramref name="slot"/> : celle déjà construite si
    /// <paramref name="key"/> (ce qui la construit) n'a pas changé, sinon une neuve.
    /// </summary>
    private UIElement Kept(string slot, string key, Func<UIElement> build)
    {
        if (_built.TryGetValue(slot, out (string Key, UIElement Element) known) && string.Equals(known.Key, key, StringComparison.Ordinal))
        {
            return known.Element;
        }

        UIElement element = build();
        _built[slot] = (key, element);
        return element;
    }

    /// <summary>
    /// Les lignes du menu deviennent <paramref name="rows"/>. Une ligne gardée à
    /// sa place ne bouge pas, une ligne refaite remplace l'ancienne sur place ;
    /// seul un décalage (focus, bords dépliés) fait sortir puis revenir la
    /// suite : une ligne qui entre dans l'arbre ou en sort coûte des ms (n° 46b).
    /// </summary>
    private void Place(List<UIElement> rows)
    {
        UIElementCollection children = RowsPanel.Children;
        int same = 0;

        while (same < rows.Count && same < children.Count)
        {
            if (!ReferenceEquals(children[same], rows[same]))
            {
                // Une ligne neuve à la place d'une ligne qui ne sert plus : échange sur place.
                bool fresh = VisualTreeHelper.GetParent(rows[same]) is null;
                bool dropped = !rows.Contains(children[same]);

                if (!fresh || !dropped)
                {
                    break;
                }

                children[same] = rows[same];
            }

            same++;
        }

        while (children.Count > same)
        {
            children.RemoveAt(children.Count - 1);
        }

        for (int i = same; i < rows.Count; i++)
        {
            children.Add(rows[i]);
        }
    }

    /// <summary>Une ligne de 36 DIPs : icône 16, libellé, raccourci ou chevron à droite.</summary>
    private Button Row(
        string glyph,
        string label,
        string actionId,
        string? value = null,
        IReadOnlyList<string>? keys = null,
        string? chevron = null,
        bool enabled = true,
        string? trailing = null)
    {
        var content = new Grid { ColumnSpacing = 12 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Icône en pixels, comme les pastilles : le menu parle la langue de la notch.
        content.Children.Add(new GlyphView { Key = glyph, Size = 16, Tint = Brush("NfTextSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center });

        var text = new TextBlock { Text = label, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(text, 1);
        content.Children.Add(text);

        FrameworkElement? right = null;

        if (keys is { Count: > 0 })
        {
            var caps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };

            foreach (string key in keys)
            {
                caps.Children.Add(KeyCap(key));
            }

            right = caps;
        }
        else if (chevron is not null)
        {
            right = new GlyphView { Key = chevron, Size = 12, Tint = Brush("NfTextTertiaryBrush"), VerticalAlignment = VerticalAlignment.Center };
        }
        else if (trailing is not null)
        {
            right = new TextBlock { Text = trailing, FontSize = 11.5, Foreground = Brush("NfTextTertiaryBrush"), VerticalAlignment = VerticalAlignment.Center };
        }

        if (right is not null)
        {
            Grid.SetColumn(right, 2);
            content.Children.Add(right);
        }

        var button = new Button
        {
            Style = (Style)Application.Current.Resources["NfMenuRowButtonStyle"],
            Content = content,
            Tag = SpaceNotch_App.UI.ThemeBrushes.Get("NfSelectionBrush"),
            IsEnabled = enabled,
            Name = actionId
        };

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => Raise(actionId, value);

        return button;
    }

    /// <summary>
    /// Minuteur : la ligne lance 15 min ; au survol ou au clavier, les puces
    /// 5 / 15 min et « Focus 25 min » apparaissent dans la ligne (140 ms, glissement de 8 DIPs,
    /// 20 ms d'écart). Un minuteur en cours se propose à l'arrêt.
    /// </summary>
    private Grid TimerRow(bool running, bool focusActive)
    {
        var host = new Grid();

        if (running)
        {
            _hideTimerChips = null;
            host.Children.Add(Row("Stop", French ? "Arrêter le minuteur" : "Stop timer", QuickMenuFeature.TimerAction, value: "0"));
            return host;
        }

        QuickMenuTile timer = QuickMenuTiles.For(QuickMenuFeature.TimerAction)!;
        Button row = Row(timer.IconKey, timer.Label, QuickMenuFeature.TimerAction, value: timer.Value, chevron: "ChevronRight");
        host.Children.Add(row);

        var chips = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Visibility = Visibility.Collapsed
        };

        foreach (int minutes in TimerChoices)
        {
            var chip = new Button
            {
                Style = (Style)Application.Current.Resources[minutes == 15 ? "NfPrimaryChipButtonStyle" : "NfChipButtonStyle"],
                Content = $"{minutes} min"
            };

            string captured = minutes.ToString(CultureInfo.InvariantCulture);
            chip.Click += (_, _) => Raise(QuickMenuFeature.TimerAction, captured);
            chips.Children.Add(chip);
        }

        // Le Focus est un minuteur de 25 min qui finit avant la prochaine réunion :
        // une puce du Minuteur, plus une fonction à part (n° 75).
        // Pas pendant un focus : un clic remplacerait sans prévenir la session en cours.
        if (!focusActive)
        {
            var focus = new Button { Style = (Style)Application.Current.Resources["NfChipButtonStyle"], Content = "Focus 25 min" };
            focus.Click += (_, _) => Raise(QuickMenuFeature.TimerAction, FocusValue);
            chips.Children.Add(focus);
        }

        host.Children.Add(chips);

        // Le chevron s'efface sous les puces : « Focus 25 min » le touchait.
        UIElement? chevron = (row.Content as Grid)?.Children.FirstOrDefault(c => c is GlyphView && Grid.GetColumn((FrameworkElement)c) == 2);

        void Reveal(bool show)
        {
            if (show == (chips.Visibility == Visibility.Visible))
            {
                return;
            }

            chips.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

            if (chevron is not null)
            {
                chevron.Opacity = show ? 0 : 1;
            }

            if (show)
            {
                PlaySlideIn(chips.Children, TimeSpan.FromMilliseconds(140), 8, TimeSpan.FromMilliseconds(20), horizontal: true);
            }
        }

        _hideTimerChips = () => Reveal(false);
        host.PointerEntered += (_, _) => Reveal(true);
        host.PointerExited += (_, _) => Reveal(false);
        row.GotFocus += (_, _) => Reveal(true);

        return host;
    }

    /// <summary>« Accrocher à… » déplié : trois puces, le bord actuel en cyan.</summary>
    private StackPanel DockChoices(QuickMenuPayload payload)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Height = QuickMenuLayout.DockChoices,
            Padding = new Thickness(38, 4, 0, 6)
        };

        (NotchEdge Edge, string Label)[] edges =
        [
            (NotchEdge.Left, French ? "← À gauche" : "← Left"),
            (NotchEdge.Top, French ? "↑ En haut" : "↑ Top"),
            (NotchEdge.Right, French ? "→ À droite" : "→ Right")
        ];

        foreach ((NotchEdge edge, string label) in edges)
        {
            if (!payload.SideEdgesAllowed && edge != NotchEdge.Top)
            {
                continue;
            }

            bool current = !payload.IsFloating && edge == payload.Edge;

            var chip = new Button
            {
                Style = (Style)Application.Current.Resources[current ? "NfActiveChipButtonStyle" : "NfChipButtonStyle"],
                Content = label
            };

            string captured = edge.ToString();
            chip.Click += (_, _) => Raise(QuickMenuFeature.DockAction, captured);
            panel.Children.Add(chip);
        }

        PlaySlideIn(panel.Children, TimeSpan.FromMilliseconds(140), 4, TimeSpan.FromMilliseconds(20), horizontal: false);
        return panel;
    }

    private static Border Separator() => new()
    {
        Height = 1,
        Margin = new Thickness(10, 4, 10, 4),
        Background = Brush("NfStrokeSubtleBrush")
    };

    /// <summary>La grille 3×3 du logo : cyan, pixel central blanc.</summary>
    private void BuildLogo()
    {
        for (int i = 0; i < 3; i++)
        {
            LogoGrid.RowDefinitions.Add(new RowDefinition());
            LogoGrid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                bool center = row == 1 && column == 1;
                var pixel = new Border
                {
                    Margin = new Thickness(0.5),
                    CornerRadius = new CornerRadius(1),
                    Background = center ? new SolidColorBrush(Colors.White) : Brush("NfActiveBrush")
                };

                Grid.SetRow(pixel, row);
                Grid.SetColumn(pixel, column);
                LogoGrid.Children.Add(pixel);
            }
        }
    }

    private void UpdateClock() => ClockText.Text = DateTime.Now.ToString("t", CultureInfo.CurrentCulture);

    // ------------------------------------------------------------------
    // Clavier
    // ------------------------------------------------------------------

    /// <summary>
    /// ↑/↓ parcourent les lignes ; les touches traitées ici ne remontent pas à
    /// la notch, qui ferait défiler ses activités. Échap, lui, remonte : il
    /// referme.
    /// </summary>
    private void OnMenuKeyDown(object sender, KeyRoutedEventArgs e)
    {
        FocusNavigationDirection? direction = e.Key switch
        {
            VirtualKey.Down => FocusNavigationDirection.Down,
            VirtualKey.Up => FocusNavigationDirection.Up,
            VirtualKey.Left => FocusNavigationDirection.Left,
            VirtualKey.Right => FocusNavigationDirection.Right,
            _ => null
        };

        if (direction is null)
        {
            return;
        }

        e.Handled = true;
        FocusManager.TryMoveFocus(direction.Value, new FindNextElementOptions { SearchRoot = RowsPanel.XamlRoot?.Content });
    }

    private void FocusRow(string name)
    {
        foreach (UIElement child in RowsPanel.Children)
        {
            if (FindFirstButton(child) is Button button && button.Name == name)
            {
                button.Focus(FocusState.Keyboard);
                return;
            }
        }
    }

    private static Button? FindFirstButton(UIElement element) => element switch
    {
        Button button => button,
        Panel panel when panel.Children.Count > 0 => FindFirstButton(panel.Children[0]),
        _ => null
    };

    // ------------------------------------------------------------------
    // Mouvement
    // ------------------------------------------------------------------

    /// <summary>Lignes du menu : 160 ms, montée de 4 DIPs, 15 ms d'écart.</summary>
    private void PlayCascade() => PlaySlideIn(RowsPanel.Children, TimeSpan.FromMilliseconds(160), 4, TimeSpan.FromMilliseconds(15), horizontal: false, initialDelay: TimeSpan.FromMilliseconds(80));

    private static void PlaySlideIn(
        UIElementCollection children,
        TimeSpan duration,
        float distance,
        TimeSpan stagger,
        bool horizontal,
        TimeSpan initialDelay = default)
    {
        if (!MotionSettings.AnimationsEnabled)
        {
            return;
        }

        int order = 0;

        foreach (UIElement child in children)
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(child);
            Compositor compositor = visual.Compositor;
            ElementCompositionPreview.SetIsTranslationEnabled(child, true);

            CompositionEasingFunction decelerate = compositor.CreateCubicBezierEasingFunction(new Vector2(0f, 0f), new Vector2(0f, 1f));
            TimeSpan delay = initialDelay + (stagger * order++);

            ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0f, 0f);
            fade.InsertKeyFrame(1f, 1f, decelerate);
            fade.Duration = duration;
            fade.DelayTime = delay;
            fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

            Vector3KeyFrameAnimation slide = compositor.CreateVector3KeyFrameAnimation();
            slide.InsertKeyFrame(0f, horizontal ? new Vector3(distance, 0, 0) : new Vector3(0, distance, 0));
            slide.InsertKeyFrame(1f, Vector3.Zero, decelerate);
            slide.Duration = duration;
            slide.DelayTime = delay;
            slide.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

            visual.StartAnimation("Opacity", fade);
            visual.StartAnimation("Translation", slide);
        }
    }

    // ------------------------------------------------------------------

    private static Border KeyCap(string key) => new()
    {
        Style = (Style)Application.Current.Resources["NfKeyCapStyle"],
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = key, Style = (Style)Application.Current.Resources["NfKeyCapTextStyle"] }
    };

    private static Brush Brush(string key)
        => SpaceNotch_App.UI.ThemeBrushes.Get(key, new SolidColorBrush(Colors.White));

    private void Raise(string actionId, string? value = null)
    {
        if (_activityId is null)
        {
            return;
        }

        ActionRequested?.Invoke(this, new IslandActionRequest(_activityId, actionId, value));
    }
}
