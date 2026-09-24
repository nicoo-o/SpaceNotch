using System;
using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Motion;
using SpaceNotch_App.Views;

namespace SpaceNotch_App.Views.Scenes;

/// <summary>
/// Historique du presse-papier.
///
/// La scène affiche et transmet ; elle ne détient aucune donnée. Coller, épingler
/// et supprimer sont trois demandes d'action, exécutées par la fonctionnalité
/// propriétaire — seule détentrice du contenu réel.
/// </summary>
public sealed partial class ClipboardScene : UserControl, IIslandSceneView
{
    public const string PasteAction = "clipboard.paste";

    public const string PinAction = "clipboard.pin";

    public const string DeleteAction = "clipboard.remove";

    private string? _activityId;

    /// <summary>Balayage en cours, s'il y en a un : une seule ligne à la fois.</summary>
    private RowSwipe? _swipe;

    /// <summary>
    /// Entrée dont le prochain clic doit être ignoré : un balayage relâché sur
    /// la ligne ne doit pas coller ce qu'il voulait supprimer.
    /// </summary>
    private string? _suppressClickFor;

    /// <summary>Faux quand Windows demande la réduction des animations : la ligne part ou revient sans glisser.</summary>
    public bool AnimateSwipe { get; set; } = true;

    public ClipboardScene()
    {
        InitializeComponent();
    }

    public event EventHandler<IslandActionRequest>? ActionRequested;

    public FrameworkElement Root => this;

    public void Apply(IslandActivity activity)
    {
        _activityId = activity.Id;

        if (activity.Payload is not ClipboardPayload payload)
        {
            return;
        }

        EntriesListView.ItemsSource = payload.Entries;
        CountText.Text = $"{payload.Entries.Count} élément{(payload.Entries.Count > 1 ? "s" : string.Empty)}";

        bool empty = payload.Entries.Count == 0;

        EntriesListView.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPasteClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ClipboardEntry entry }
            && string.Equals(_suppressClickFor, entry.Id, StringComparison.Ordinal))
        {
            _suppressClickFor = null;
            return;
        }

        RaiseFor(sender, PasteAction);
    }

    private void OnPinClicked(object sender, RoutedEventArgs e)
        => RaiseFor(sender, PinAction);

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
        => RaiseFor(sender, DeleteAction);

    /// <summary>
    /// Retrouve l'entrée visée par le contexte de données de la ligne : c'est
    /// l'identifiant de l'entrée, jamais son contenu, qui est transmis.
    /// </summary>
    private void RaiseFor(object sender, string actionId)
    {
        if (_activityId is null)
        {
            return;
        }

        if (sender is not FrameworkElement { DataContext: ClipboardEntry entry })
        {
            return;
        }

        ActionRequested?.Invoke(this, new IslandActionRequest(_activityId, actionId, entry.Id));
    }

    // ------------------------------------------------------------------
    // Balayer pour supprimer
    // ------------------------------------------------------------------

    /// <summary>Une ligne en cours de balayage et ce qu'on sait du geste.</summary>
    private sealed class RowSwipe
    {
        public required Grid Row { get; init; }

        public required FrameworkElement Content { get; init; }

        public required UIElement Backdrop { get; init; }

        public required ClipboardEntry Entry { get; init; }

        public required Pointer Pointer { get; init; }

        public double StartX { get; init; }

        public double StartY { get; init; }

        public bool Swiping { get; set; }

        public double Offset { get; set; }

        public VelocityTracker Velocity { get; } = new();

        public Stopwatch Clock { get; } = Stopwatch.StartNew();
    }

    private void OnRowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Grid row || row.Tag is "swipe")
        {
            return;
        }

        // Les boutons de la ligne marquent l'appui comme traité : le balayage
        // l'observe quand même, sans rien leur retirer tant qu'il n'a pas
        // commencé.
        row.Tag = "swipe";
        row.AddHandler(PointerPressedEvent, new PointerEventHandler(OnRowPointerPressed), handledEventsToo: true);
        row.AddHandler(PointerMovedEvent, new PointerEventHandler(OnRowPointerMoved), handledEventsToo: true);
        row.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnRowPointerReleased), handledEventsToo: true);
        row.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnRowPointerCanceled), handledEventsToo: true);
        row.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnRowPointerCanceled), handledEventsToo: true);

        ResetRow(row);
    }

    /// <summary>
    /// Un conteneur recyclé par la liste reçoit une autre entrée : il doit
    /// repartir à sa place, faute de quoi une ligne supprimée laisserait son
    /// décalage à la suivante.
    /// </summary>
    private void OnRowDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is not Grid row)
        {
            return;
        }

        // Un balayage commencé sur l'ancienne entrée ne vaut rien pour la nouvelle.
        if (ReferenceEquals(_swipe?.Row, row))
        {
            _swipe = null;
        }

        ResetRow(row);
    }

    private void OnRowPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Grid { Children.Count: 2, DataContext: ClipboardEntry entry } row
            || row.Children[1] is not FrameworkElement content)
        {
            return;
        }

        global::Windows.Foundation.Point point = e.GetCurrentPoint(this).Position;

        _swipe = new RowSwipe
        {
            Row = row,
            Content = content,
            Backdrop = row.Children[0],
            Entry = entry,
            Pointer = e.Pointer,
            StartX = point.X,
            StartY = point.Y
        };
    }

    private void OnRowPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_swipe is not { } swipe || !ReferenceEquals(sender, swipe.Row) || e.Pointer.PointerId != swipe.Pointer.PointerId)
        {
            return;
        }

        global::Windows.Foundation.Point point = e.GetCurrentPoint(this).Position;
        double dx = point.X - swipe.StartX;
        double dy = point.Y - swipe.StartY;

        if (!swipe.Swiping)
        {
            if (!SwipeToDelete.Starts(dx, dy))
            {
                return;
            }

            // Le geste devient un balayage : la ligne prend le pointeur au
            // bouton, qui perd son état enfoncé et ne collera rien.
            swipe.Swiping = true;
            _suppressClickFor = swipe.Entry.Id;
            swipe.Row.CapturePointer(e.Pointer);
            ElementCompositionPreview.SetIsTranslationEnabled(swipe.Content, true);
        }

        double width = swipe.Row.ActualWidth;
        swipe.Offset = SwipeToDelete.Offset(dx, width);
        swipe.Velocity.Add(swipe.Clock.Elapsed.TotalSeconds, point.X, point.Y);

        Visual visual = ElementCompositionPreview.GetElementVisual(swipe.Content);
        visual.StopAnimation("Translation");
        visual.Properties.InsertVector3("Translation", new Vector3((float)swipe.Offset, 0, 0));
        swipe.Backdrop.Opacity = SwipeToDelete.Reveal(swipe.Offset, width);

        e.Handled = true;
    }

    private void OnRowPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_swipe is not { } swipe || !ReferenceEquals(sender, swipe.Row))
        {
            return;
        }

        _swipe = null;

        if (!swipe.Swiping)
        {
            return;
        }

        e.Handled = true;
        swipe.Row.ReleasePointerCapture(swipe.Pointer);

        (double velocity, _) = swipe.Velocity.Velocity(swipe.Clock.Elapsed.TotalSeconds);
        double width = swipe.Row.ActualWidth;

        if (SwipeToDelete.Decide(swipe.Offset, width, velocity) == SwipeOutcome.Delete)
        {
            Dismiss(swipe, width);
        }
        else
        {
            SpringBack(swipe.Content, swipe.Backdrop);
        }
    }

    private void OnRowPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        // Une capture perdue n'est pas une décision : la ligne revient.
        if (_swipe is not { } swipe || !ReferenceEquals(sender, swipe.Row))
        {
            return;
        }

        _swipe = null;

        if (swipe.Swiping)
        {
            SpringBack(swipe.Content, swipe.Backdrop);
        }
    }

    /// <summary>La ligne part vers la gauche, puis l'entrée est supprimée par sa fonctionnalité.</summary>
    private void Dismiss(RowSwipe swipe, double width)
    {
        string activityId = _activityId ?? string.Empty;
        string entryId = swipe.Entry.Id;

        void Remove() => ActionRequested?.Invoke(this, new IslandActionRequest(activityId, DeleteAction, entryId));

        if (!AnimateSwipe || _activityId is null)
        {
            if (_activityId is not null)
            {
                Remove();
            }

            return;
        }

        Visual visual = ElementCompositionPreview.GetElementVisual(swipe.Content);
        Compositor compositor = visual.Compositor;

        Vector3KeyFrameAnimation exit = compositor.CreateVector3KeyFrameAnimation();
        exit.InsertKeyFrame(
            1f,
            new Vector3((float)-(width + 16), 0, 0),
            compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.8f), new Vector2(0.3f, 1f)));
        exit.Duration = TimeSpan.FromMilliseconds(180);

        CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation("Translation", exit);
        batch.End();
        batch.Completed += (_, _) => DispatcherQueue.TryEnqueue(Remove);
    }

    /// <summary>La ligne revient à sa place, par un ressort sans rebond : rien n'a eu lieu.</summary>
    private void SpringBack(FrameworkElement content, UIElement backdrop)
    {
        backdrop.Opacity = 0;

        Visual visual = ElementCompositionPreview.GetElementVisual(content);

        if (!AnimateSwipe)
        {
            visual.StopAnimation("Translation");
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
            return;
        }

        SpringVector3NaturalMotionAnimation spring = visual.Compositor.CreateSpringVector3Animation();
        spring.FinalValue = Vector3.Zero;
        spring.DampingRatio = 0.85f;
        spring.Period = TimeSpan.FromMilliseconds(45);

        visual.StartAnimation("Translation", spring);
    }

    private static void ResetRow(Grid row)
    {
        if (row.Children.Count != 2 || row.Children[1] is not FrameworkElement content)
        {
            return;
        }

        row.Children[0].Opacity = 0;

        ElementCompositionPreview.SetIsTranslationEnabled(content, true);
        Visual visual = ElementCompositionPreview.GetElementVisual(content);
        visual.StopAnimation("Translation");
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
    }
}
