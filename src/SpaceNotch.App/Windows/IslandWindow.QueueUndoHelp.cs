using System;
using System.Collections.Generic;
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
                    : ((SolidColorBrush)SpaceNotch_App.UI.ThemeBrushes.Get(RootLayout, "NfTextSecondaryBrush")).Color)
            };

            // Un point de plus : il apparaît d'une pichenette.
            if (i >= _queueShown && UseSpringAnimations())
            {
                dot.Opacity = 0;
                _ = _dispatcherQueue.TryEnqueueSafely(DispatcherQueuePriority.Low, () => dot.Opacity = 1);
            }

            QueueDotsRow.Children.Add(dot);
        }

        _queueShown = count;

        // Les points ont un nom : « 3 activités en attente » (phase C).
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(QueueDotsRow, count == 0
            ? string.Empty
            : Lang.T(count == 1 ? "1 activité en attente" : $"{count} activités en attente", count == 1 ? "1 activity waiting" : $"{count} activities waiting"));
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

        AnnounceText(Lang.T(
            $"{activity.Title} écarté. Cliquer sur la notch dans les 3 secondes pour annuler.",
            $"{activity.Title} dismissed. Click the notch within 3 seconds to undo."));
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
    private IReadOnlyList<GestureTip> _helpTips = [];
    private long _altSince;
    private readonly TextBlock _measureHelp = new() { FontSize = 11 };

    private void ArmGestureHelp(bool hovering)
    {
        if (!hovering)
        {
            _helpTimer?.Stop();

            // La rangée d'une leçon n'appartient pas à l'aide Alt : la sortie du
            // pointeur ne l'efface pas, le minuteur de la leçon s'en charge.
            if (_lessonKey is null)
            {
                SetGestureHelp(false);
            }

            return;
        }

        _altSince = 0;
        _helpTimer ??= CreateRepeatingTimer(TimeSpan.FromMilliseconds(100), () =>
        {
            // Alt doit rester enfoncé un instant au survol : un Alt bref, qui
            // vise la barre de menus de l'application, ne montre rien.
            // Pendant une leçon, la rangée est à elle : sans cette garde, le
            // pointeur qui entrait pour essayer le geste l'effaçait en 100 ms.
            if (_lessonKey is not null)
            {
                return;
            }

            long now = Environment.TickCount64;
            bool alt = NativeMethods.IsKeyDown(AltKey);
            _altSince = alt ? (_altSince == 0 ? now : _altSince) : 0;

            SetGestureHelp(alt
                && now - _altSince >= (long)NotchGestures.HelpDelay.TotalMilliseconds
                && _controller.State is IslandState.Closed or IslandState.Preview);
        });
        _helpTimer.Start();
    }

    private void SetGestureHelp(bool shown, IReadOnlyList<GestureTip>? tips = null)
    {
        if (shown == _helpShown)
        {
            return;
        }

        // Alt tenu : toute l'aide ; une leçon (ADR-028) : le seul geste enseigné.
        _helpShown = shown;
        _helpTips = shown ? tips ?? GestureHelp.For(_controller.PresentedActivity) : [];
        GestureHelpRow.Children.Clear();

        if (shown)
        {
            foreach (GestureTip tip in _helpTips)
            {
                // Jetons du thème de la notch (phase F) : en apparence claire,
                // l'aide restait blanche sur fond clair.
                var text = new TextBlock { FontSize = 11, Foreground = SpaceNotch_App.UI.ThemeBrushes.Get(RootLayout, "NfTextSecondaryBrush") };
                text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = tip.Gesture, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = SpaceNotch_App.UI.ThemeBrushes.Get(RootLayout, "NfTextPrimaryBrush") });
                text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " · " + tip.Effect });
                GestureHelpRow.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(999),
                    Padding = new Thickness(7, 3, 7, 3),
                    Background = SpaceNotch_App.UI.ThemeBrushes.Get(RootLayout, "NfStrokeSubtleBrush"),
                    Child = text
                });
            }
        }

        GestureHelpRow.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

        if (shown)
        {
            AnnounceText((tips is null ? string.Empty : Lang.T("Astuce : ", "Tip: ")) + string.Join(", ", _helpTips.Select(t => t.Gesture + " : " + t.Effect)));
        }

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

        foreach (GestureTip tip in _helpTips)
        {
            width += Measure(_measureHelp, tip.Gesture + " · " + tip.Effect) + 14 + 6;
        }

        return new IslandFootprint(Math.Max(rest.Width, width), rest.Height + 26);
    }

    // ---- Presse-papier en pile ----------------------------------------------

    private bool _clipStackHintGiven;

    /// <summary>Ctrl + molette : ouvre la pile ou la fait défiler d'un cran.</summary>
    private bool CycleClipStack(int delta)
    {
        // Historique désactivé (c'est le réglage par défaut) : pas de pile, et un clic dessus ne mènerait à rien.
        if (_clipboardFeature.State != SpaceNotch.Core.Features.FeatureState.Running)
        {
            // Une fois par session : la pile restait muette, sans dire pourquoi.
            if (!_clipStackHintGiven)
            {
                _clipStackHintGiven = true;
                AnnounceText(Lang.T(
                    "Historique du presse-papier désactivé : activez-le dans les réglages pour parcourir la pile.",
                    "Clipboard history is off: turn it on in settings to browse the stack."));
            }

            return false;
        }

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

    // ---- Gestes enseignés (ADR-028, volet A) ---------------------------------

    private readonly HashSet<string> _lessonsShown = [];
    private string? _lessonKey;
    private DispatcherQueueTimer? _lessonTimer;
    private readonly HashSet<string> _learnedThisSession = [];

    /// <summary>
    /// Au repos, un geste devenu utile s'enseigne une fois par session dans la
    /// rangée d'aide (« Molette · volume » à la première musique), le temps de
    /// le lire. Alt tenu au survol continue de tout montrer.
    /// </summary>
    private void ConsiderGestureLesson(IslandActivity? activity)
    {
        // Ni languette (la rangée la déformerait), ni retrait, ni verrouillage
        // (consommée sans être vue), ni visite filmée.
        if (_helpShown || !_islandShown || _sessionLocked || _touring || UsesSideTab)
        {
            return;
        }

        if (GestureCoach.Next(activity, _controller.State == IslandState.Closed, _settings.LearnedGestures, _lessonsShown) is not { } lesson)
        {
            return;
        }

        _lessonKey = lesson.Key;

        // Hors du rendu en cours : la rangée change la forme et redemande un rendu.
        // Comptée « montrée » seulement quand elle s'affiche vraiment : remplacée
        // avant, elle restait due.
        _ = _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_lessonKey == lesson.Key && !_isClosed && _controller.State == IslandState.Closed)
            {
                _lessonsShown.Add(lesson.Key);
                SetGestureHelp(true, [lesson.Tip]);
            }
            else if (_lessonKey == lesson.Key)
            {
                _lessonKey = null;
            }
        });

        _lessonTimer ??= CreateOneShotTimer(GestureCoach.ShowFor, EndGestureLesson);
        _lessonTimer.Stop();
        _lessonTimer.Interval = GestureCoach.ShowFor;
        _lessonTimer.Start();
    }

    private void EndGestureLesson()
    {
        if (_lessonKey is null)
        {
            return;
        }

        _lessonKey = null;
        _lessonTimer?.Stop();
        SetGestureHelp(false);
    }

    /// <summary>Un geste utilisé est appris : la notch ne l'enseignera plus.</summary>
    private void LearnGesture(string key)
    {
        if (_lessonKey == key)
        {
            EndGestureLesson();
        }

        // Une fois par session au plus : la molette tourne par dizaines de crans,
        // et chaque enregistrement réécrit le fichier de réglages.
        if (!_learnedThisSession.Add(key) || _settings.LearnedGestures.Contains(key, StringComparer.Ordinal))
        {
            return;
        }

        _settingsService.Update(s => s.LearnedGestures.Add(key));
    }
}
