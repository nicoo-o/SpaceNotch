using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Localization;

namespace SpaceNotch_App.Views.Scenes;

/// <summary>
/// Presse-papier en pile (vague 7) : la carte de devant en pleine lumière,
/// deux autres qui dépassent derrière, plus sombres. Ctrl + molette les fait
/// défiler (la fenêtre s'en charge) ; un clic recolle l'élément de devant.
/// </summary>
public sealed partial class ClipStackScene : Grid, IIslandSceneView
{
    private const double CardHeight = 34;
    private const double Peek = 5;

    private static readonly global::Windows.UI.Color Mint = Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x7F, 0xE8, 0xB0);

    private readonly Grid _stack = new() { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(22, 12, 22, 0) };
    private readonly TextBlock _status = new() { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 26, 6), IsHitTestVisible = false };
    private readonly List<Border> _cards = [];
    private string? _activityId;

    public ClipStackScene()
    {
        Children.Add(_stack);
        Children.Add(_status);
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        // Joignable au clavier et nommée (phase C) : Entrée ou Espace recolle,
        // comme le clic.
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationControlType(this, Microsoft.UI.Xaml.Automation.Peers.AutomationControlType.Button);

        Tapped += (_, e) =>
        {
            if (Paste())
            {
                e.Handled = true;
            }
        };

        KeyDown += (_, e) =>
        {
            if (e.Key is global::Windows.System.VirtualKey.Enter or global::Windows.System.VirtualKey.Space && Paste())
            {
                e.Handled = true;
            }
        };
    }

    private bool Paste()
    {
        if (_activityId is null)
        {
            return false;
        }

        ActionRequested?.Invoke(this, new IslandActionRequest(_activityId, SpaceNotch.Features.Clipboard.ClipboardFeature.StackPasteAction));
        return true;
    }

    public event EventHandler<IslandActionRequest>? ActionRequested;

    public FrameworkElement Root => this;

    public void Apply(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        _activityId = activity.Id;

        if (activity.Payload is not ClipStackPayload stack || stack.Entries.Count == 0)
        {
            return;
        }

        // De derrière vers devant : la dernière ajoutée est au-dessus.
        _stack.Children.Clear();
        _cards.Clear();
        int shown = Math.Min(stack.Entries.Count, ClipStackPayload.Behind + 1);

        for (int depth = shown - 1; depth >= 0; depth--)
        {
            ClipboardEntry entry = stack.Entries[(stack.Index + depth) % stack.Entries.Count];
            Border card = Card(entry, depth, stack.Recalled && depth == 0);
            _stack.Children.Add(card);
            _cards.Add(card);
        }

        _status.Text = stack.Recalled ? "✓ " + Lang.T("Recollé", "Pasted again") : $"{stack.Index + 1} / {stack.Entries.Count}";

        ClipboardEntry front = stack.Entries[stack.Index % stack.Entries.Count];
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, Lang.T(
            $"Presse-papier, {KindLabel(front.Kind)} {stack.Index + 1} sur {stack.Entries.Count} : {front.Preview}",
            $"Clipboard, {KindLabel(front.Kind)} {stack.Index + 1} of {stack.Entries.Count}: {front.Preview}"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(this, Lang.T(
            "Entrée pour recoller, Ctrl et molette pour parcourir.",
            "Enter to paste again, Ctrl and wheel to browse."));
        _status.Foreground = new SolidColorBrush(stack.Recalled ? Mint : Microsoft.UI.ColorHelper.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    }

    private static Border Card(ClipboardEntry entry, int depth, bool recalled)
    {
        var kind = new TextBlock
        {
            Text = KindLabel(entry.Kind),
            FontSize = 11,
            Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x8C, 0xFF, 0xFF, 0xFF))
        };
        var text = new TextBlock
        {
            Text = entry.Preview,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xEB, 0xFF, 0xFF, 0xFF)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };

        var content = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(kind);
        content.Children.Add(text);

        double scale = 1 - (depth * 0.07);
        return new Border
        {
            Height = CardHeight,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 0, 10, 0),
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, (byte)(0x14 - (depth * 4)), (byte)(0x16 - (depth * 4)), (byte)(0x1C - (depth * 4)))),
            BorderBrush = new SolidColorBrush(recalled ? Mint : Microsoft.UI.ColorHelper.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Opacity = depth == 0 ? 1 : 1 - (depth * 0.3),
            Child = depth == 0 ? content : null,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0),
            RenderTransform = new CompositeTransform { ScaleX = scale, ScaleY = scale, TranslateY = depth * Peek }
        };
    }

    private static string KindLabel(string kind) => kind switch
    {
        "link" => Lang.T("Lien", "Link"),
        "image" => Lang.T("Image", "Image"),
        "files" => Lang.T("Fichiers", "Files"),
        _ => Lang.T("Texte", "Text")
    };
}
