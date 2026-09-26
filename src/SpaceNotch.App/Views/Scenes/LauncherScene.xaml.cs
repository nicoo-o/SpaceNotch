using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Launcher;
using SpaceNotch_App.Launcher;
using SpaceNotch_App.UI;
using Windows.System;
using Windows.UI.Core;

namespace SpaceNotch_App.Views.Scenes;

/// <summary>
/// Recherche façon Spotlight / Raycast : un champ, des groupes de résultats, une
/// pilule de sélection qui glisse, un panneau d'actions (Ctrl+K ou clic droit).
///
/// La recherche elle-même — sources, classement, groupes — est faite par la
/// fonctionnalité : la vue transmet la saisie et dessine ce qu'on lui publie.
/// Seules la sélection et le panneau d'actions vivent ici.
/// </summary>
public sealed partial class LauncherScene : UserControl, IIslandSceneView
{
    public const string SearchAction = "launcher.search";
    public const string OpenAction = "launcher.open";
    public const string PinAction = "launcher.pin";
    public const string LocationAction = "launcher.location";
    public const string AdminAction = "launcher.admin";
    public const string UninstallAction = "launcher.uninstall";
    public const string DismissAction = "launcher.dismiss";
    public const string ActionsPanelAction = "launcher.actions";

    /// <summary>Ancien nom, gardé pour les appelants existants.</summary>
    public const string LaunchAction = OpenAction;

    private const double ActionRowHeight = 36;

    private static readonly bool French = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";

    private readonly List<RowSlot> _rows = [];
    private readonly List<ActionSlot> _actionRows = [];

    private LauncherIconCache? _icons;
    private string? _activityId;
    private string _query = string.Empty;
    private string _signature = string.Empty;
    private IReadOnlyCollection<string> _favorites = [];
    private int _selected = -1;
    private bool _searchFocused;
    private bool _filterFocused;
    private bool _cascadePending = true;

    private LauncherResult? _actionsFor;
    private int _actionSelected;

    public LauncherScene()
    {
        InitializeComponent();

        ActionsFilter.GotFocus += (_, _) => _filterFocused = true;
        ActionsFilter.LostFocus += (_, _) => _filterFocused = false;

        // À chaque ouverture, la liste arrive en cascade et la sélection
        // repart du haut ; le panneau d'actions ne survit pas à une fermeture.
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) =>
        {
            if (Visibility == Visibility.Visible)
            {
                _cascadePending = true;
                _signature = string.Empty;
            }
            else
            {
                CloseActions(notify: false, refocus: false);
            }
        });

        BuildFooter();
    }

    public event EventHandler<IslandActionRequest>? ActionRequested;

    public FrameworkElement Root => this;

    /// <summary>Vrai tant que l'utilisateur tape : dans le champ ou dans le filtre des actions.</summary>
    public bool IsEditing => _searchFocused || _filterFocused;

    /// <summary>Met le focus dans le champ : on ouvre la recherche pour taper.</summary>
    public void FocusSearch()
    {
        if (ActionsPanel.Visibility == Visibility.Visible)
        {
            ActionsFilter.Focus(FocusState.Programmatic);
            return;
        }

        SearchBox.Focus(FocusState.Programmatic);
        SearchBox.SelectionStart = SearchBox.Text.Length;
    }

    public void Apply(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        _activityId = activity.Id;

        if (activity.Payload is not LauncherPayload payload)
        {
            return;
        }

        // Le champ n'est réécrit que s'il a divergé, et jamais pendant la
        // frappe : le réécrire replacerait le curseur et ferait sauter la
        // saisie en cours.
        if (!_searchFocused && !string.Equals(SearchBox.Text, payload.Query, StringComparison.Ordinal))
        {
            SearchBox.Text = payload.Query ?? string.Empty;
        }

        _query = payload.Query ?? string.Empty;
        _favorites = payload.Favorites ?? (IReadOnlyCollection<string>)[];

        HotkeyText.Text = payload.Hotkey is { Length: > 0 } hotkey
            ? (French ? $"{hotkey} pour rouvrir" : $"{hotkey} to reopen")
            : string.Empty;

        // Les lignes ne sont reconstruites que si les résultats ont changé :
        // chaque republication les recréait, et la sélection sautait.
        string signature = Signature(payload);

        if (signature != _signature)
        {
            string? previous = Selected?.Id;
            _signature = signature;
            BuildRows(payload.Sections);

            // La sélection suit le résultat qu'elle désignait s'il est encore
            // là ; sinon elle revient au premier — c'est lui qu'Entrée ouvre.
            int keep = previous is null ? -1 : _rows.FindIndex(r => r.Result.Id == previous);
            Select(keep >= 0 && _query.Length == 0 ? keep : (_rows.Count > 0 ? 0 : -1), animate: false);
        }

        UpdateEmptyState(payload);

        // Le résultat du panneau a disparu (une autre recherche) : le panneau
        // n'a plus d'objet.
        if (_actionsFor is not null && !_rows.Any(r => r.Result.Id == _actionsFor.Id))
        {
            CloseActions(notify: true, refocus: true);
        }
        else if (_actionsFor is not null)
        {
            _actionsFor = _rows.First(r => r.Result.Id == _actionsFor.Id).Result;
            BuildActionRows();
        }
    }

    // ------------------------------------------------------------------
    // Lignes
    // ------------------------------------------------------------------

    private LauncherResult? Selected => _selected >= 0 && _selected < _rows.Count ? _rows[_selected].Result : null;

    private static string Signature(LauncherPayload payload)
    {
        var builder = new StringBuilder(256);
        builder.Append(payload.Query).Append('\u001f');

        foreach (LauncherSection section in payload.Sections)
        {
            builder.Append(section.Title).Append('\u001e');

            foreach (LauncherResult item in section.Items)
            {
                builder.Append(item.Id).Append('\u001d').Append(item.Title).Append('\u001d');

                foreach (TextMatch match in item.Matches)
                {
                    builder.Append(match.Start).Append(',').Append(match.Length).Append(';');
                }
            }
        }

        if (payload.Favorites is { } favorites)
        {
            builder.Append('\u001c').Append(favorites.Count);
        }

        return builder.ToString();
    }

    private void BuildRows(IReadOnlyList<LauncherSection> sections)
    {
        RowsPanel.Children.Clear();
        _rows.Clear();

        _icons ??= new LauncherIconCache(DispatcherQueue);

        double top = 0;
        int index = 0;

        foreach (LauncherSection section in sections)
        {
            RowsPanel.Children.Add(new TextBlock
            {
                Text = section.Title,
                Height = LauncherLayout.SectionHeader,
                Padding = new Thickness(12, 9, 12, 0),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("NfTextTertiaryBrush"),
                IsHitTestVisible = false
            });

            top += LauncherLayout.SectionHeader;

            foreach (LauncherResult item in section.Items)
            {
                double height = item.Kind == LauncherResultKind.Calculation ? LauncherLayout.CalculationRow : LauncherLayout.Row;
                RowSlot slot = BuildRow(item, index, top, height);

                RowsPanel.Children.Add(slot.Element);
                _rows.Add(slot);

                top += height;
                index++;
            }
        }

        if (_cascadePending && _rows.Count > 0)
        {
            _cascadePending = false;
            PlayCascade();
        }
    }

    private RowSlot BuildRow(LauncherResult item, int index, double top, double height)
    {
        bool calculation = item.Kind == LauncherResultKind.Calculation;

        var grid = new Grid
        {
            Height = height,
            Padding = new Thickness(12, 0, 12, 0),
            ColumnSpacing = 12,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
        };

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        FrameworkElement icon = BuildIcon(item);
        grid.Children.Add(icon);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = calculation ? 2 : 0 };
        Grid.SetColumn(texts, 1);

        var title = new TextBlock
        {
            FontSize = calculation ? 22 : 13.5,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        FillTitle(title, item);
        texts.Children.Add(title);

        string subtitle = item.Subtitle;

        if (_favorites.Contains(item.Id) && item.Kind != LauncherResultKind.Calculation)
        {
            subtitle = subtitle.Length > 0 ? $"★  {subtitle}" : "★";
        }

        if (subtitle.Length > 0)
        {
            texts.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 11.5,
                Foreground = Brush("NfTextTertiaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap
            });
        }

        grid.Children.Add(texts);

        // L'indice « Ouvrir ↵ » n'apparaît que sur la ligne sélectionnée.
        var hint = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Opacity = 0 };
        hint.Children.Add(new TextBlock
        {
            Text = HintFor(item),
            FontSize = 11.5,
            Foreground = Brush("NfTextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        hint.Children.Add(KeyCap("↵"));
        hint.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(120) };
        Grid.SetColumn(hint, 2);
        grid.Children.Add(hint);

        AutomationPropertiesHelper.SetName(grid, item.Subtitle.Length > 0 ? $"{item.Title}, {item.Subtitle}" : item.Title);

        int captured = index;
        grid.PointerMoved += (_, _) =>
        {
            if (_selected != captured && ActionsPanel.Visibility != Visibility.Visible)
            {
                Select(captured, animate: true);
            }
        };
        grid.Tapped += (_, e) =>
        {
            e.Handled = true;

            if (ActionsPanel.Visibility == Visibility.Visible)
            {
                CloseActions(notify: true, refocus: true);
                return;
            }

            Select(captured, animate: true);
            Invoke(LauncherAction.Open);
        };
        grid.RightTapped += (_, e) =>
        {
            e.Handled = true;
            Select(captured, animate: true);
            OpenActions();
        };

        return new RowSlot(item, grid, hint, top, height);
    }

    private FrameworkElement BuildIcon(LauncherResult item)
    {
        var fallback = new FontIcon
        {
            Glyph = GlyphFor(item.Kind),
            FontSize = item.Kind == LauncherResultKind.Calculation ? 18 : 16,
            Foreground = Brush(item.Kind == LauncherResultKind.Calculation ? "NfActiveBrush" : "NfTextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (item.IconPath is not { Length: > 0 } path || _icons is null)
        {
            return fallback;
        }

        var host = new Grid { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
        var image = new Image { Width = 24, Height = 24, Stretch = Stretch.Uniform };
        host.Children.Add(fallback);
        host.Children.Add(image);

        ImageSource? known = _icons.Get(path, ready =>
        {
            image.Source = ready;
            fallback.Visibility = Visibility.Collapsed;
        });

        if (known is not null)
        {
            image.Source = known;
            fallback.Visibility = Visibility.Collapsed;
        }

        return host;
    }

    /// <summary>
    /// Le nom, avec les lettres trouvées en gras blanc et le reste un ton plus
    /// bas : on voit d'un coup d'œil pourquoi la ligne est là.
    /// </summary>
    private static void FillTitle(TextBlock block, LauncherResult item)
    {
        Brush strong = Brush("NfTextPrimaryBrush");

        if (item.Matches.Count == 0 || item.Kind == LauncherResultKind.Calculation)
        {
            block.Foreground = strong;
            block.Text = item.Title;
            return;
        }

        Brush soft = Brush("NfTextSecondaryBrush");
        int position = 0;

        foreach (TextMatch match in item.Matches.OrderBy(m => m.Start))
        {
            if (match.Start < position || match.Start + match.Length > item.Title.Length)
            {
                continue;
            }

            if (match.Start > position)
            {
                block.Inlines.Add(new Run { Text = item.Title[position..match.Start], Foreground = soft });
            }

            block.Inlines.Add(new Run
            {
                Text = item.Title.Substring(match.Start, match.Length),
                Foreground = strong,
                FontWeight = FontWeights.Bold
            });

            position = match.Start + match.Length;
        }

        if (position < item.Title.Length)
        {
            block.Inlines.Add(new Run { Text = item.Title[position..], Foreground = soft });
        }
    }

    private static string GlyphFor(LauncherResultKind kind) => kind switch
    {
        LauncherResultKind.Setting => "",
        LauncherResultKind.File => "",
        LauncherResultKind.Calculation => "",
        LauncherResultKind.Web => "",
        _ => ""
    };

    private static string HintFor(LauncherResult item) => item.Kind switch
    {
        LauncherResultKind.Calculation => French ? "Copier" : "Copy",
        LauncherResultKind.Web => French ? "Rechercher" : "Search",
        _ => French ? "Ouvrir" : "Open"
    };

    private void UpdateEmptyState(LauncherPayload payload)
    {
        bool empty = _rows.Count == 0;

        ListScroller.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

        if (!empty)
        {
            return;
        }

        EmptyTitle.Text = payload.IsLoading
            ? (French ? "Chargement des applications…" : "Loading apps…")
            : _query.Length > 0
                ? (French ? $"Aucun résultat pour « {_query} »" : $"No results for “{_query}”")
                : (French ? "Tapez pour chercher une application, un réglage, un fichier ou un calcul." : "Type to find an app, a setting, a file or a calculation.");
    }

    // ------------------------------------------------------------------
    // Sélection : la pilule glisse (ressort 0,22 s, amortissement 0,85)
    // ------------------------------------------------------------------

    private void Select(int index, bool animate)
    {
        if (_selected >= 0 && _selected < _rows.Count)
        {
            _rows[_selected].Hint.Opacity = 0;
        }

        _selected = index;

        if (index < 0 || index >= _rows.Count)
        {
            SelectionPill.Opacity = 0;
            return;
        }

        RowSlot slot = _rows[index];
        slot.Hint.Opacity = 1;
        SelectionPill.Height = slot.Height;
        SelectionPill.Opacity = 1;

        MovePill(SelectionPill, (float)slot.Top, animate);
        KeepVisible(slot);
    }

    private static void MovePill(UIElement pill, float y, bool animate)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(pill, true);
        Visual visual = ElementCompositionPreview.GetElementVisual(pill);

        if (!animate || !MotionSettings.AnimationsEnabled)
        {
            visual.StopAnimation("Translation");
            visual.Properties.InsertVector3("Translation", new Vector3(0, y, 0));
            return;
        }

        SpringVector3NaturalMotionAnimation spring = visual.Compositor.CreateSpringVector3Animation();
        spring.FinalValue = new Vector3(0, y, 0);
        spring.DampingRatio = 0.85f;
        spring.Period = TimeSpan.FromMilliseconds(35); // réponse ≈ 0,22 s
        spring.StopBehavior = AnimationStopBehavior.SetToFinalValue;
        visual.StartAnimation("Translation", spring);
    }

    private void KeepVisible(RowSlot slot)
    {
        double viewport = ListScroller.ViewportHeight;

        if (viewport <= 0)
        {
            return;
        }

        double offset = ListScroller.VerticalOffset;
        double top = slot.Top + 6; // marge haute de la liste
        double bottom = top + slot.Height;

        if (top - LauncherLayout.SectionHeader < offset)
        {
            ListScroller.ChangeView(null, Math.Max(0, top - LauncherLayout.SectionHeader - 6), null);
        }
        else if (bottom > offset + viewport)
        {
            ListScroller.ChangeView(null, bottom - viewport + 6, null);
        }
    }

    private void Move(int delta)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        int index = _selected < 0
            ? (delta > 0 ? 0 : _rows.Count - 1)
            : (_selected + delta + _rows.Count) % _rows.Count;

        Select(index, animate: true);
    }

    /// <summary>
    /// Cascade d'entrée : chaque ligne arrive 18 ms après la précédente, par un
    /// fondu et une montée de 6 DIPs, après 80 ms — le temps que la forme ait
    /// pris sa place.
    /// </summary>
    private void PlayCascade()
    {
        if (!MotionSettings.AnimationsEnabled)
        {
            return;
        }

        int order = 0;

        foreach (UIElement child in RowsPanel.Children)
        {
            TimeSpan delay = TimeSpan.FromMilliseconds(80 + (order * 18));
            order = Math.Min(order + 1, 12);

            Visual visual = ElementCompositionPreview.GetElementVisual(child);
            Compositor compositor = visual.Compositor;
            ElementCompositionPreview.SetIsTranslationEnabled(child, true);

            CompositionEasingFunction decelerate = compositor.CreateCubicBezierEasingFunction(new Vector2(0f, 0f), new Vector2(0f, 1f));
            TimeSpan duration = TimeSpan.FromMilliseconds(220);

            ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0f, 0f);
            fade.InsertKeyFrame(1f, 1f, decelerate);
            fade.Duration = duration;
            fade.DelayTime = delay;
            fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

            Vector3KeyFrameAnimation rise = compositor.CreateVector3KeyFrameAnimation();
            rise.InsertKeyFrame(0f, new Vector3(0, 6, 0));
            rise.InsertKeyFrame(1f, Vector3.Zero, decelerate);
            rise.Duration = duration;
            rise.DelayTime = delay;
            rise.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

            visual.StartAnimation("Opacity", fade);
            visual.StartAnimation("Translation", rise);
        }
    }

    // ------------------------------------------------------------------
    // Clavier
    // ------------------------------------------------------------------

    private static bool IsDown(VirtualKey key)
        => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        // Seule la saisie de l'utilisateur relance une recherche : le texte
        // réinjecté par Apply, lui, correspond déjà à la requête publiée.
        if (string.Equals(SearchBox.Text, _query, StringComparison.Ordinal))
        {
            return;
        }

        _query = SearchBox.Text;
        Raise(SearchAction, SearchBox.Text);
    }

    private void OnSearchGotFocus(object sender, RoutedEventArgs e) => _searchFocused = true;

    private void OnSearchLostFocus(object sender, RoutedEventArgs e) => _searchFocused = false;

    /// <summary>
    /// ↑/↓ et Tab parcourent la liste sans quitter le champ ; Entrée ouvre,
    /// Ctrl+Maj+Entrée ouvre en administrateur ; Ctrl+K ouvre le panneau
    /// d'actions, Ctrl+P épingle, Ctrl+O montre l'emplacement. Échap vide la
    /// requête avant de refermer. Les touches traitées ne remontent pas à la
    /// notch, qui les prendrait pour les siennes.
    /// </summary>
    private void OnSearchPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool control = IsDown(VirtualKey.Control);
        bool shift = IsDown(VirtualKey.Shift);

        switch (e.Key)
        {
            case VirtualKey.Down:
                Move(1);
                break;

            case VirtualKey.Up:
                Move(-1);
                break;

            case VirtualKey.Tab:
                Move(shift ? -1 : 1);
                break;

            case VirtualKey.Enter when control && shift:
                Invoke(LauncherAction.RunAsAdministrator);
                break;

            case VirtualKey.Enter:
                Invoke(LauncherAction.Open);
                break;

            case VirtualKey.K when control:
                OpenActions();
                break;

            case VirtualKey.P when control:
                Invoke(LauncherAction.TogglePin);
                break;

            case VirtualKey.O when control:
                Invoke(LauncherAction.OpenLocation);
                break;

            case VirtualKey.Escape when SearchBox.Text.Length > 0:
                SearchBox.Text = string.Empty;
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    // ------------------------------------------------------------------
    // Actions
    // ------------------------------------------------------------------

    private void Invoke(LauncherAction action)
    {
        LauncherResult? result = Selected;

        if (result is null)
        {
            // Entrée dans un champ vide sans rien de sélectionné : rien, plutôt
            // qu'une application tirée au hasard.
            return;
        }

        if (!LauncherActions.For(result).Contains(action))
        {
            return;
        }

        switch (action)
        {
            case LauncherAction.Open when result.Kind == LauncherResultKind.Calculation:
                CopyToClipboard(result.Target);
                Raise(DismissAction);
                break;

            case LauncherAction.Open:
                Raise(OpenAction, result.Id);
                break;

            case LauncherAction.TogglePin:
                Raise(PinAction, result.Id);
                break;

            case LauncherAction.OpenLocation:
                Raise(LocationAction, result.Target);
                break;

            case LauncherAction.RunAsAdministrator:
                Raise(AdminAction, result.Id);
                break;

            case LauncherAction.Uninstall:
                Raise(UninstallAction, result.Id);
                break;
        }
    }

    private static void CopyToClipboard(string text)
    {
        try
        {
            var package = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        catch (Exception)
        {
            // Presse-papier occupé par une autre application : rien à faire.
        }
    }

    private void OpenActions()
    {
        LauncherResult? result = Selected;

        if (result is null || !result.HasActions)
        {
            return;
        }

        _actionsFor = result;
        _actionSelected = 0;
        ActionsTitle.Text = result.Title;
        ActionsFilter.Text = string.Empty;
        BuildActionRows();

        bool wasOpen = ActionsPanel.Visibility == Visibility.Visible;
        ActionsPanel.Visibility = Visibility.Visible;

        if (!wasOpen)
        {
            Raise(ActionsPanelAction, "1");
            PlayPanelEntrance();
        }

        ActionsFilter.Focus(FocusState.Programmatic);
    }

    private void CloseActions(bool notify, bool refocus)
    {
        if (_actionsFor is null && ActionsPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        _actionsFor = null;
        ActionsPanel.Visibility = Visibility.Collapsed;

        if (notify)
        {
            Raise(ActionsPanelAction, "0");
        }

        if (refocus)
        {
            SearchBox.Focus(FocusState.Programmatic);
        }
    }

    private void BuildActionRows()
    {
        ActionRows.Children.Clear();
        _actionRows.Clear();

        if (_actionsFor is null)
        {
            return;
        }

        bool pinned = _favorites.Contains(_actionsFor.Id);
        string filter = ActionsFilter.Text.Trim();

        foreach (LauncherAction action in LauncherActions.For(_actionsFor))
        {
            (string label, string glyph, string keys) = Describe(action, pinned);

            if (filter.Length > 0 && LauncherRanking.Score(label, filter, out _) <= 0)
            {
                continue;
            }

            bool danger = action == LauncherAction.Uninstall;

            if (danger && _actionRows.Count > 0)
            {
                ActionRows.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(4, 4, 4, 4),
                    Background = Brush("NfStrokeSubtleBrush")
                });
            }

            double top = ActionRows.Children.OfType<FrameworkElement>().Sum(c => c.Height + c.Margin.Top + c.Margin.Bottom);

            var row = new Grid { Height = ActionRowHeight, Padding = new Thickness(10, 0, 8, 0), ColumnSpacing = 10, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Brush ink = Brush(danger ? "NfDangerTextBrush" : "NfTextPrimaryBrush");

            row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14, Foreground = danger ? ink : Brush("NfTextSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center });

            var text = new TextBlock { Text = label, FontSize = 13, Foreground = ink, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            if (keys.Length > 0)
            {
                var caps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };

                foreach (string key in keys.Split(' '))
                {
                    caps.Children.Add(KeyCap(key));
                }

                Grid.SetColumn(caps, 2);
                row.Children.Add(caps);
            }

            AutomationPropertiesHelper.SetName(row, label);

            int captured = _actionRows.Count;
            row.PointerMoved += (_, _) =>
            {
                if (_actionSelected != captured)
                {
                    SelectAction(captured, animate: true);
                }
            };
            row.Tapped += (_, e) =>
            {
                e.Handled = true;
                RunAction(captured);
            };

            ActionRows.Children.Add(row);
            _actionRows.Add(new ActionSlot(action, top));
        }

        SelectAction(Math.Clamp(_actionSelected, 0, Math.Max(0, _actionRows.Count - 1)), animate: false);
    }

    private static (string Label, string Glyph, string Keys) Describe(LauncherAction action, bool pinned) => action switch
    {
        LauncherAction.Open => (French ? "Ouvrir" : "Open", "", "↵"),
        LauncherAction.TogglePin => pinned
            ? (French ? "Retirer des favoris" : "Remove from favorites", "", "Ctrl P")
            : (French ? "Épingler aux favoris" : "Pin to favorites", "", "Ctrl P"),
        LauncherAction.OpenLocation => (French ? "Ouvrir l'emplacement" : "Open file location", "", "Ctrl O"),
        LauncherAction.RunAsAdministrator => (French ? "Exécuter en administrateur" : "Run as administrator", "", "Ctrl Maj ↵"),
        LauncherAction.Uninstall => (French ? "Désinstaller…" : "Uninstall…", "", string.Empty),
        _ => (action.ToString(), "", string.Empty)
    };

    private void SelectAction(int index, bool animate)
    {
        _actionSelected = index;

        if (index < 0 || index >= _actionRows.Count)
        {
            ActionPill.Opacity = 0;
            return;
        }

        ActionPill.Opacity = 1;
        MovePill(ActionPill, (float)_actionRows[index].Top, animate);
    }

    private void RunAction(int index)
    {
        if (index < 0 || index >= _actionRows.Count)
        {
            return;
        }

        LauncherAction action = _actionRows[index].Action;
        CloseActions(notify: true, refocus: true);
        Invoke(action);
    }

    private void OnActionsFilterChanged(object sender, TextChangedEventArgs e)
    {
        _actionSelected = 0;
        BuildActionRows();
    }

    private void OnActionsPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool control = IsDown(VirtualKey.Control);

        switch (e.Key)
        {
            case VirtualKey.Down when _actionRows.Count > 0:
                SelectAction((_actionSelected + 1) % _actionRows.Count, animate: true);
                break;

            case VirtualKey.Up when _actionRows.Count > 0:
                SelectAction((_actionSelected - 1 + _actionRows.Count) % _actionRows.Count, animate: true);
                break;

            case VirtualKey.Enter:
                RunAction(_actionSelected);
                break;

            case VirtualKey.Escape:
            case VirtualKey.K when control:
                CloseActions(notify: true, refocus: true);
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>Le panneau monte de 4 DIPs en se précisant (160 ms, décélération).</summary>
    private void PlayPanelEntrance()
    {
        if (!MotionSettings.AnimationsEnabled)
        {
            return;
        }

        Visual visual = ElementCompositionPreview.GetElementVisual(ActionsPanel);
        Compositor compositor = visual.Compositor;
        ElementCompositionPreview.SetIsTranslationEnabled(ActionsPanel, true);

        CompositionEasingFunction decelerate = compositor.CreateCubicBezierEasingFunction(new Vector2(0f, 0f), new Vector2(0f, 1f));

        ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, decelerate);
        fade.Duration = TimeSpan.FromMilliseconds(160);

        Vector3KeyFrameAnimation rise = compositor.CreateVector3KeyFrameAnimation();
        rise.InsertKeyFrame(0f, new Vector3(0, 4, 0));
        rise.InsertKeyFrame(1f, Vector3.Zero, decelerate);
        rise.Duration = TimeSpan.FromMilliseconds(160);

        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Translation", rise);
    }

    // ------------------------------------------------------------------
    // Pied et petits éléments
    // ------------------------------------------------------------------

    private void BuildFooter()
    {
        FooterHints.Children.Clear();

        AddHint("↑↓", French ? "naviguer" : "navigate");
        AddHint("↵", French ? "ouvrir" : "open");
        AddHint("Ctrl K", "actions");

        void AddHint(string keys, string label)
        {
            var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };

            foreach (string key in keys.Split(' '))
            {
                group.Children.Add(KeyCap(key));
            }

            group.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11.5,
                Foreground = Brush("NfTextTertiaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });

            FooterHints.Children.Add(group);
        }
    }

    private static Border KeyCap(string key) => new()
    {
        Style = (Style)Application.Current.Resources["NfKeyCapStyle"],
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = key,
            Style = (Style)Application.Current.Resources["NfKeyCapTextStyle"]
        }
    };

    private static Brush Brush(string key)
        => Application.Current.Resources.TryGetValue(key, out object value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.White);

    private void Raise(string actionId, string? value = null)
    {
        if (_activityId is null)
        {
            return;
        }

        ActionRequested?.Invoke(this, new IslandActionRequest(_activityId, actionId, value));
    }

    private sealed record RowSlot(LauncherResult Result, FrameworkElement Element, UIElement Hint, double Top, double Height);

    private sealed record ActionSlot(LauncherAction Action, double Top);

    /// <summary>Nom lu par le Narrateur pour une ligne construite en code.</summary>
    private static class AutomationPropertiesHelper
    {
        public static void SetName(UIElement element, string name)
            => Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, name);
    }
}
