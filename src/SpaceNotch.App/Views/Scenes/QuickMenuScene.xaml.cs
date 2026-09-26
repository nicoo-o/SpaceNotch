using System;
using System.Collections.Generic;
using System.Globalization;
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
/// Menu rapide : Rechercher, Minuteur (5 / 15 / 25 min au survol),
/// Presse-papier, Étagère · Détacher, Accrocher à… (trois bords dépliés sur
/// place) · Réglages, Quitter. La vue décrit ; la fenêtre exécute.
/// </summary>
public sealed partial class QuickMenuScene : UserControl, IIslandSceneView
{
    private static readonly bool French = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";

    private static readonly int[] TimerChoices = [5, 15, 25];

    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(15) };

    private string? _activityId;
    private QuickMenuPayload? _shown;
    private bool _cascadePending = true;

    public QuickMenuScene()
    {
        InitializeComponent();

        BuildLogo();
        _clock.Tick += (_, _) => UpdateClock();

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
            }
        });
    }

    public event EventHandler<IslandActionRequest>? ActionRequested;

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
        RowsPanel.Children.Clear();

        RowsPanel.Children.Add(Row(
            "",
            French ? "Rechercher" : "Search",
            QuickMenuFeature.SearchAction,
            keys: payload.Hotkey?.Replace("Win+", "⊞+", StringComparison.Ordinal).Split('+')));

        RowsPanel.Children.Add(TimerRow(payload.TimerRunning));

        RowsPanel.Children.Add(Row(
            "",
            French ? "Presse-papier" : "Clipboard",
            QuickMenuFeature.ClipboardAction,
            enabled: payload.HasClipboard,
            trailing: payload.HasClipboard ? null : (French ? "vide" : "empty")));

        RowsPanel.Children.Add(Row(
            "",
            French ? "Étagère" : "Shelf",
            QuickMenuFeature.ShelfAction,
            enabled: payload.HasShelf,
            trailing: payload.HasShelf ? null : (French ? "vide" : "empty")));

        RowsPanel.Children.Add(Separator());

        RowsPanel.Children.Add(payload.IsFloating
            ? Row("", French ? "Raccrocher au bord" : "Reattach to edge", QuickMenuFeature.DockAction, value: payload.Edge.ToString())
            : Row("", French ? "Détacher de l'écran" : "Detach from screen", QuickMenuFeature.DetachAction));

        RowsPanel.Children.Add(Row(
            "",
            French ? "Accrocher à…" : "Dock to…",
            QuickMenuFeature.DockExpandAction,
            value: payload.DockExpanded ? "0" : "1",
            chevron: payload.DockExpanded ? "" : ""));

        if (payload.DockExpanded)
        {
            RowsPanel.Children.Add(DockChoices(payload));
        }

        RowsPanel.Children.Add(Separator());

        RowsPanel.Children.Add(Row("", French ? "Réglages" : "Settings", QuickMenuFeature.SettingsAction, keys: ["Ctrl", ","]));
        RowsPanel.Children.Add(Row("", French ? "Quitter" : "Quit", QuickMenuFeature.QuitAction));

        if (_cascadePending)
        {
            _cascadePending = false;
            PlayCascade();
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

        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14, Foreground = Brush("NfTextSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center });

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
            right = new FontIcon { Glyph = chevron, FontSize = 10, Foreground = Brush("NfTextTertiaryBrush"), VerticalAlignment = VerticalAlignment.Center };
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
            Tag = Application.Current.Resources["NfSelectionBrush"],
            IsEnabled = enabled,
            Name = actionId
        };

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => Raise(actionId, value);

        return button;
    }

    /// <summary>
    /// Minuteur : la ligne lance 15 min ; au survol ou au clavier, les puces
    /// 5 / 15 / 25 min apparaissent dans la ligne (140 ms, glissement de 8 DIPs,
    /// 20 ms d'écart). Un minuteur en cours se propose à l'arrêt.
    /// </summary>
    private Grid TimerRow(bool running)
    {
        var host = new Grid();

        if (running)
        {
            host.Children.Add(Row("", French ? "Arrêter le minuteur" : "Stop timer", QuickMenuFeature.TimerAction, value: "0"));
            return host;
        }

        Button row = Row("", French ? "Minuteur" : "Timer", QuickMenuFeature.TimerAction, value: "15", chevron: "");
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

        host.Children.Add(chips);

        void Reveal(bool show)
        {
            if (show == (chips.Visibility == Visibility.Visible))
            {
                return;
            }

            chips.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

            if (show)
            {
                PlaySlideIn(chips.Children, TimeSpan.FromMilliseconds(140), 8, TimeSpan.FromMilliseconds(20), horizontal: true);
            }
        }

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
        => Application.Current.Resources.TryGetValue(key, out object value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Colors.White);

    private void Raise(string actionId, string? value = null)
    {
        if (_activityId is null)
        {
            return;
        }

        ActionRequested?.Invoke(this, new IslandActionRequest(_activityId, actionId, value));
    }
}
