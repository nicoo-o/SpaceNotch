using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Activities;
using SpaceNotch.Features.FileShelf;
using SpaceNotch_App.Launcher;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace SpaceNotch_App.Views.Scenes;

/// <summary>
/// Étagère de fichiers : ce qui a été déposé sur la notch, prêt à être repris.
/// Un fichier se retire d'un ✕, l'étagère se vide d'un bouton — jamais les
/// fichiers eux-mêmes, qui restent où ils sont sur le disque.
/// </summary>
public sealed partial class FileShelfScene : UserControl, IIslandSceneView
{
    private static readonly bool French = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";

    private LauncherIconCache? _icons;
    private string? _activityId;

    public FileShelfScene()
    {
        InitializeComponent();

        if (!French)
        {
            ClearButton.Content = "Clear all";
            EmptyText.Text = "Drop a file on the notch";
        }
    }

    public event EventHandler<IslandActionRequest>? ActionRequested;

    public FrameworkElement Root => this;

    public void Apply(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        _activityId = activity.Id;
    }

    public void UpdateItems(IReadOnlyList<ShelfItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _icons ??= new LauncherIconCache(DispatcherQueue);

        HeaderText.Text = French
            ? $"Étagère · {items.Count} fichier{(items.Count > 1 ? "s" : string.Empty)}"
            : $"Shelf · {items.Count} file{(items.Count > 1 ? "s" : string.Empty)}";

        RowsPanel.Children.Clear();

        foreach (ShelfItem item in items)
        {
            RowsPanel.Children.Add(BuildRow(item));
        }

        bool empty = items.Count == 0;
        RowsPanel.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    private Grid BuildRow(ShelfItem item)
    {
        var row = new Grid
        {
            Height = 40,
            Padding = new Thickness(8, 0, 8, 0),
            ColumnSpacing = 10,
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Colors.Transparent),
            CanDrag = true
        };

        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var fallback = new FontIcon { Glyph = "", FontSize = 16, Foreground = Brush("NfTextSecondaryBrush") };
        var image = new Image { Width = 24, Height = 24 };
        var icon = new Grid { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
        icon.Children.Add(fallback);
        icon.Children.Add(image);

        if (_icons?.Get(item.FilePath, ready => { image.Source = ready; fallback.Visibility = Visibility.Collapsed; }) is { } known)
        {
            image.Source = known;
            fallback.Visibility = Visibility.Collapsed;
        }

        row.Children.Add(icon);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = item.FileName,
            FontSize = 12.5,
            Foreground = Brush("NfTextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        texts.Children.Add(new TextBlock
        {
            Text = item.FormattedSize,
            FontSize = 10.5,
            Foreground = Brush("NfTextTertiaryBrush")
        });
        Grid.SetColumn(texts, 1);
        row.Children.Add(texts);

        // ✕ au survol seulement : la liste reste calme tant qu'on ne vise rien.
        var remove = new Button
        {
            Style = (Style)Application.Current.Resources["NfIconButtonStyle"],
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(ColorHelper.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
            Content = new FontIcon { Glyph = "", FontSize = 9 },
            Opacity = 0,
            VerticalAlignment = VerticalAlignment.Center
        };
        remove.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(120) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, French ? $"Retirer {item.FileName}" : $"Remove {item.FileName}");
        remove.Click += (_, _) => Raise(FileShelfManager.RemoveAction, item.Id);
        Grid.SetColumn(remove, 3);
        row.Children.Add(remove);

        // Partager sur le téléphone (F8) : un QR code, au survol comme le ✕.
        var share = new Button
        {
            Style = (Style)Application.Current.Resources["NfIconButtonStyle"],
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(ColorHelper.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
            Content = new GlyphView { Key = "Qr", Size = 10, Tint = Brush("NfTextPrimaryBrush") },
            Opacity = 0,
            VerticalAlignment = VerticalAlignment.Center
        };
        share.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(120) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(share, French ? $"Partager {item.FileName} sur le téléphone" : $"Share {item.FileName} to your phone");
        ToolTipService.SetToolTip(share, French ? "Partager sur le téléphone" : "Share to your phone");
        share.Click += (_, _) => Raise(FileShelfManager.ShareAction, item.Id);
        Grid.SetColumn(share, 2);
        row.Children.Add(share);

        Brush hover = Brush("NfSelectionBrush");
        row.PointerEntered += (_, _) => { row.Background = hover; remove.Opacity = 1; share.Opacity = 1; };
        row.PointerExited += (_, _) => { row.Background = new SolidColorBrush(Colors.Transparent); remove.Opacity = 0; share.Opacity = 0; };
        remove.GotFocus += (_, _) => remove.Opacity = 1;
        share.GotFocus += (_, _) => share.Opacity = 1;

        row.DragStarting += (_, e) => OfferFile(e, item.FilePath);

        return row;
    }

    /// <summary>
    /// Glisser un fichier hors de l'étagère le donne à l'application qui le
    /// reçoit. Le fichier n'est ouvert qu'au moment du dépôt — un fournisseur
    /// différé —, jamais au début du geste.
    /// </summary>
    private static void OfferFile(DragStartingEventArgs e, string path)
    {
        e.Data.RequestedOperation = DataPackageOperation.Copy;
        e.Data.SetDataProvider(StandardDataFormats.StorageItems, async request =>
        {
            DataProviderDeferral deferral = request.GetDeferral();

            try
            {
                request.SetData(new List<IStorageItem> { await StorageFile.GetFileFromPathAsync(path) });
            }
            catch (Exception)
            {
                // Fichier déplacé ou supprimé depuis son dépôt : rien à donner.
                request.SetData(new List<IStorageItem>());
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    private void OnClearClicked(object sender, RoutedEventArgs e) => Raise(FileShelfManager.ClearAction);

    private static Brush Brush(string key)
        => Application.Current.Resources.TryGetValue(key, out object value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Colors.White);

    private void Raise(string actionId, string? value = null)
        => ActionRequested?.Invoke(this, new IslandActionRequest(_activityId ?? FileShelfManager.ShelfActivityId, actionId, value));
}
