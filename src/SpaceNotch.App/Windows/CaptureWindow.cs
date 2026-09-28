using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using SpaceNotch.Core.Capture;
using SpaceNotch.Platform.Windows.Capture;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Capture de texte (W4) : l'écran est figé tel qu'il était, assombri, et on
/// y trace un rectangle. Au relâché, la fenêtre se retire et remet le
/// morceau d'image choisi ; Échap ou un clic droit annulent.
///
/// <para>
/// La fenêtre ne lit rien elle-même : elle rend un <see cref="ScreenImage"/>
/// découpé, l'OCR se fait ailleurs. Rien n'est écrit sur le disque.
/// </para>
/// </summary>
internal sealed class CaptureWindow : Window
{
    private readonly ScreenImage _screen;
    private readonly Action<ScreenImage?> _done;
    private readonly Grid _root = new();
    private readonly RectangleGeometry _hole = new();
    private readonly Rectangle _frame = new()
    {
        Stroke = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x7F, 0xE6, 0xFF)),
        StrokeThickness = 1.5,
        Visibility = Visibility.Collapsed,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        IsHitTestVisible = false
    };

    private global::Windows.Foundation.Point? _anchor;
    private global::Windows.Foundation.Rect _selection;
    private bool _finished;

    private CaptureWindow(ScreenImage screen, Action<ScreenImage?> done)
    {
        _screen = screen;
        _done = done;

        Title = "SpaceNotch";

        var bitmap = new WriteableBitmap(screen.Width, screen.Height);

        using (Stream pixels = bitmap.PixelBuffer.AsStream())
        {
            pixels.Write(screen.Pixels, 0, screen.Pixels.Length);
        }

        bitmap.Invalidate();

        var dim = new GeometryGroup { FillRule = FillRule.EvenOdd };
        dim.Children.Add(new RectangleGeometry { Rect = new global::Windows.Foundation.Rect(0, 0, 100000, 100000) });
        dim.Children.Add(_hole);

        var hint = new TextBlock
        {
            Text = SpaceNotch.Core.Localization.Lang.T("Tracer un rectangle autour du texte · Échap pour annuler", "Draw a rectangle around the text · Esc to cancel"),
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 48),
            IsHitTestVisible = false
        };

        _root.Background = new SolidColorBrush(Colors.Black);
        _root.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Fill });
        _root.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = dim, Fill = new SolidColorBrush(ColorHelper.FromArgb(0x8C, 0x00, 0x00, 0x00)), IsHitTestVisible = false });
        _root.Children.Add(_frame);
        _root.Children.Add(hint);
        _root.IsTabStop = true;

        _root.PointerPressed += OnPressed;
        _root.PointerMoved += OnMoved;
        _root.PointerReleased += OnReleased;
        _root.KeyDown += OnKeyDown;
        _root.Loaded += (_, _) => _root.Focus(FocusState.Programmatic);

        Content = _root;
        Closed += (_, _) => Finish(null);

        OverlappedPresenter presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(screen.X, screen.Y, screen.Width, screen.Height));
    }

    /// <summary>
    /// Fige l'écran sous le pointeur et ouvre la sélection. <paramref name="done"/>
    /// reçoit le morceau choisi, ou <c>null</c> si l'utilisateur a annulé.
    /// </summary>
    public static bool Open(Action<ScreenImage?> done)
    {
        ArgumentNullException.ThrowIfNull(done);

        SpaceNotch.Platform.Windows.Win32.NativeMethods.GetCursorPos(out var cursor);
        DisplayArea area = DisplayArea.GetFromPoint(new global::Windows.Graphics.PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary);
        global::Windows.Graphics.RectInt32 bounds = area.OuterBounds;

        if (ScreenGrab.Capture(bounds.X, bounds.Y, bounds.Width, bounds.Height) is not { } screen)
        {
            return false;
        }

        var window = new CaptureWindow(screen, done);
        window.Activate();
        return true;
    }

    private double Scale => _root.XamlRoot?.RasterizationScale ?? 1.0;

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_root);

        if (point.Properties.IsRightButtonPressed)
        {
            Finish(null);
            return;
        }

        _anchor = point.Position;
        _root.CapturePointer(e.Pointer);
        Update(point.Position);
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_anchor is not null)
        {
            Update(e.GetCurrentPoint(_root).Position);
        }
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_anchor is null)
        {
            return;
        }

        Update(e.GetCurrentPoint(_root).Position);
        _anchor = null;
        _root.ReleasePointerCapture(e.Pointer);

        double scale = Scale;
        int x = (int)Math.Round(_selection.X * scale);
        int y = (int)Math.Round(_selection.Y * scale);
        int width = (int)Math.Round(_selection.Width * scale);
        int height = (int)Math.Round(_selection.Height * scale);

        if (!OcrText.IsUsable(width, height))
        {
            // Un clic n'est pas une sélection : on recommence.
            _frame.Visibility = Visibility.Collapsed;
            _hole.Rect = default;
            return;
        }

        Finish(_screen.Crop(x, y, width, height));
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == global::Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            Finish(null);
        }
    }

    private void Update(global::Windows.Foundation.Point current)
    {
        if (_anchor is not { } anchor)
        {
            return;
        }

        var (x, y, width, height) = OcrText.Selection((int)anchor.X, (int)anchor.Y, (int)current.X, (int)current.Y);
        _selection = new global::Windows.Foundation.Rect(x, y, width, height);
        _hole.Rect = _selection;

        _frame.Visibility = Visibility.Visible;
        _frame.Margin = new Thickness(x, y, 0, 0);
        _frame.Width = Math.Max(1, width);
        _frame.Height = Math.Max(1, height);
    }

    private void Finish(ScreenImage? result)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;

        try
        {
            Close();
        }
        catch (Exception)
        {
            // Déjà fermée : le résultat part quand même.
        }

        _done(result);
    }
}
