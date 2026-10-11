using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpaceNotch.Core.Activities;
using SpaceNotch.Features.Productivity;

namespace SpaceNotch_App.Views.Scenes;

/// <summary>
/// Note éclair (F7) : la note s'écrit dans la notch ; elle s'enregistre 400 ms
/// après la dernière frappe, et à la fermeture.
/// </summary>
public sealed partial class NoteScene : UserControl, IIslandSceneView
{
    /// <summary>La zone de texte : l'œil droit y devient le curseur (vague 7).</summary>
    public FrameworkElement Field => NoteBox;

    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    private DispatcherQueueTimer? _saveTimer;
    private string? _activityId;
    private bool _loading;

    public NoteScene()
    {
        InitializeComponent();
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) =>
        {
            if (Visibility != Visibility.Visible)
            {
                SaveNow();
            }
        });
    }

    public event EventHandler<IslandActionRequest>? ActionRequested;

    /// <summary>
    /// Enregistre tout de suite, sans attendre le délai de frappe. À appeler
    /// avant de ranger la note : une fois l'activité retirée, plus aucune
    /// fonctionnalité ne revendique l'enregistrement, et les dernières
    /// frappes (moins de 400 ms) étaient perdues.
    /// </summary>
    public void Flush() => SaveNow();

    public FrameworkElement Root => this;

    public void Apply(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        _activityId = activity.Id;

        // Le texte n'est posé qu'à l'ouverture : réécrire le champ pendant la
        // frappe ferait sauter le curseur.
        if (activity.Payload is NotePayload note && NoteBox.FocusState == FocusState.Unfocused && !string.Equals(NoteBox.Text, note.Text, StringComparison.Ordinal))
        {
            _loading = true;
            NoteBox.Text = note.Text;
            NoteBox.SelectionStart = note.Text.Length;
            _loading = false;
        }
    }

    /// <summary>Le curseur dans la note, à la fin du texte.</summary>
    public void FocusNote()
    {
        NoteBox.Focus(FocusState.Programmatic);
        NoteBox.SelectionStart = NoteBox.Text.Length;
    }

    private void OnNoteChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _saveTimer ??= CreateSaveTimer();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private DispatcherQueueTimer CreateSaveTimer()
    {
        DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
        timer.Interval = SaveDelay;
        timer.IsRepeating = false;
        timer.Tick += SpaceNotch_App.Diagnostics.Guard.Tick((_, _) => SaveNow());
        return timer;
    }

    private void SaveNow()
    {
        _saveTimer?.Stop();

        if (_activityId is not null)
        {
            ActionRequested?.Invoke(this, new IslandActionRequest(_activityId, NoteFeature.SaveAction, NoteBox.Text));
        }
    }
}
