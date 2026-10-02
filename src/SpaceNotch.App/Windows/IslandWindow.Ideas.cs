using System;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Features.Channel;
using SpaceNotch.Features.Clipboard;
using SpaceNotch.Platform.Windows.Win32;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Vague 7, l'interface : la file d'attente en points, « Annuler » pendant 3 s,
/// l'aide des gestes avec Alt, et le presse-papier en pile (Ctrl + molette).
/// </summary>
public sealed partial class IslandWindow
{
    // ---- File d'attente visible ---------------------------------------------

    private int _queueShown;

    /// <summary>Un point par activité qui attend, teinté comme elle, au bas de la notch.</summary>
    private void UpdateQueueDots(int waiting)
    {
        int count = QueueDots.Count(waiting);
        bool expanded = _controller.State is IslandState.Expanded or IslandState.Expanding;
        QueueDotsRow.Visibility = count > 0 && !expanded ? Visibility.Visible : Visibility.Collapsed;

        if (count == _queueShown && QueueDotsRow.Children.Count == count)
        {
            return;
        }

        IslandActivity? presented = _controller.PresentedActivity;
        var waitingActivities = _activityManager.GetActiveActivities()
            .Where(a => presented is null || !string.Equals(a.Id, presented.Id, StringComparison.Ordinal))
            .Take(count)
            .ToList();

        QueueDotsRow.Children.Clear();

        for (int i = 0; i < count; i++)
        {
            ActivityTint? tint = i < waitingActivities.Count ? waitingActivities[i].Tint : null;
            var dot = new Ellipse
            {
                Width = QueueDots.Size,
                Height = QueueDots.Size,
                Fill = new SolidColorBrush(tint is { } t
                    ? global::Windows.UI.Color.FromArgb(0xFF, t.R, t.G, t.B)
                    : Microsoft.UI.ColorHelper.FromArgb(0x99, 0xFF, 0xFF, 0xFF))
            };

            // Un point de plus : il apparaît d'une pichenette.
            if (i >= _queueShown && UseSpringAnimations())
            {
                dot.Opacity = 0;
                _ = _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => dot.Opacity = 1);
            }

            QueueDotsRow.Children.Add(dot);
        }

        _queueShown = count;
    }

    // ---- Annuler en 3 s -----------------------------------------------------

    private readonly UndoShelf _undo = new();
    private bool _undoClipboard;
    private DispatcherQueueTimer? _undoTimer;

    private bool UndoVisible => _undo.CanUndo(DateTimeOffset.UtcNow) && _controller.PresentedActivity is null && !UsesSideTab;

    /// <summary>Ce qui va être écarté par cette action, s'il est rattrapable.</summary>
    private IslandActivity? UndoableBefore(IslandActionRequest request)
    {
        bool dismiss = request.ActionId == ChannelFeature.DismissAction
            || request.ActionId == ClipboardFeature.RemoveAction
            || request.ActionId.EndsWith(".dismiss", StringComparison.Ordinal)
            || request.ActionId.EndsWith(".ignore", StringComparison.Ordinal);

        if (!dismiss || request.ActionId == SpaceNotch.Features.Launcher.LauncherFeature.DismissAction)
        {
            return null;
        }

        return _activityManager.GetActiveActivities().FirstOrDefault(a => string.Equals(a.Id, request.ActivityId, StringComparison.Ordinal));
    }

    private void OfferUndo(IslandActionRequest request, IslandActivity activity)
    {
        _undoClipboard = request.ActionId == ClipboardFeature.RemoveAction;
        _undo.Offer(activity, DateTimeOffset.UtcNow);
        _undoTimer ??= CreateRepeatingTimer(TimeSpan.FromMilliseconds(50), UndoTick);
        _undoTimer.Start();
        RequestRender();
    }

    private void UndoTick()
    {
        double remaining = _undo.Remaining(DateTimeOffset.UtcNow);
        UndoBarScale.ScaleX = remaining;

        if (remaining <= 0)
        {
            _undoTimer?.Stop();
            _undo.Clear();
            RequestRender();
        }
    }

    /// <summary>Au repos, « Annuler » et sa barre remplacent les yeux le temps de l'annulation.</summary>
    private void ShowUndo()
    {
        if (!UndoVisible)
        {
            return;
        }

        UndoText.Text = "↶ " + Lang.T("Annuler", "Undo");
        IdleRestView.Visibility = Visibility.Collapsed;
        ShowRestPixel(false);
        UndoRestView.Visibility = Visibility.Visible;
    }

    /// <summary>Le clic rattrape ce qui vient d'être écarté.</summary>
    private bool TryUndo()
    {
        if (!UndoVisible)
        {
            return false;
        }

        IslandActivity? activity = _undo.Take(DateTimeOffset.UtcNow);
        _undoTimer?.Stop();

        if (activity is null)
        {
            return false;
        }

        if (_undoClipboard)
        {
            _ = _clipboardFeature.UndoRemove();
        }
        else
        {
            _activityManager.PostActivity(activity);
            _activityManager.PinPresentation(activity.Id);
        }

        RequestRender();
        return true;
    }

    // ---- Aide des gestes avec Alt ------------------------------------------

    private const int AltKey = 0x12;
    private DispatcherQueueTimer? _helpTimer;
    private bool _helpShown;
    private readonly TextBlock _measureHelp = new() { FontSize = 10 };

    private void ArmGestureHelp(bool hovering)
    {
        if (!hovering)
        {
            _helpTimer?.Stop();
            SetGestureHelp(false);
            return;
        }

        _helpTimer ??= CreateRepeatingTimer(TimeSpan.FromMilliseconds(120), () =>
            SetGestureHelp(NativeMethods.IsKeyDown(AltKey)
                && _controller.State is IslandState.Closed or IslandState.Preview));
        _helpTimer.Start();
    }

    private void SetGestureHelp(bool shown)
    {
        if (shown == _helpShown)
        {
            return;
        }

        _helpShown = shown;
        GestureHelpRow.Children.Clear();

        if (shown)
        {
            foreach (GestureTip tip in GestureHelp.For(_controller.PresentedActivity))
            {
                var text = new TextBlock { FontSize = 10, Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x99, 0xFF, 0xFF, 0xFF)) };
                text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = tip.Gesture, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xEB, 0xFF, 0xFF, 0xFF)) });
                text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " · " + tip.Effect });
                GestureHelpRow.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(999),
                    Padding = new Thickness(7, 3, 7, 3),
                    Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x12, 0xFF, 0xFF, 0xFF)),
                    Child = text
                });
            }
        }

        GestureHelpRow.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

        // Le contenu remonte pour laisser la rangée en dessous (la rangée reste dans la notch).
        var lift = new System.Numerics.Vector3(0, shown ? -13 : 0, 0);
        SignalRestView.Translation = lift;
        CardRestView.Translation = lift;
        IdleRestView.Translation = lift;
        RequestRender();
    }

    /// <summary>La forme grandit pour accueillir la rangée d'aide.</summary>
    private IslandFootprint WithGestureHelp(IslandFootprint rest)
    {
        if (!_helpShown)
        {
            return rest;
        }

        double width = 40;

        foreach (GestureTip tip in GestureHelp.For(_controller.PresentedActivity))
        {
            width += Measure(_measureHelp, tip.Gesture + " · " + tip.Effect) + 14 + 6;
        }

        return new IslandFootprint(Math.Max(rest.Width, width), rest.Height + 26);
    }

    // ---- Presse-papier en pile ----------------------------------------------

    /// <summary>Ctrl + molette : ouvre la pile ou la fait défiler d'un cran.</summary>
    private bool CycleClipStack(int delta)
    {
        bool open = string.Equals(_controller.PresentedActivity?.Id, ClipboardFeature.StackActivityId, StringComparison.Ordinal);

        if (!_clipboardFeature.ShowStack(open ? (delta > 0 ? -1 : 1) : 0))
        {
            return false;
        }

        if (!open)
        {
            _activityManager.PinPresentation(ClipboardFeature.StackActivityId);
            _controller.RequestExpand();
        }

        _diagnostics.CountEvent();
        return true;
    }
}
