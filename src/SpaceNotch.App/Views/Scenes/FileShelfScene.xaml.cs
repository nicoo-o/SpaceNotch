using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpaceNotch.Core.Activities;
using SpaceNotch.Features.FileShelf;
using SpaceNotch_App.Views;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace SpaceNotch_App.Views.Scenes;

/// <summary>
/// Étagère de fichiers : ce qui a été déposé sur la notch, prêt à être repris.
/// </summary>
public sealed partial class FileShelfScene : UserControl, IIslandSceneView
{
    public FileShelfScene()
    {
        InitializeComponent();
    }

    public FrameworkElement Root => this;

    public void Apply(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
    }

    public void UpdateItems(IReadOnlyList<ShelfItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        FilesListView.ItemsSource = items;
        CountText.Text = items.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);

        bool empty = items.Count == 0;
        FilesListView.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Glisser un fichier hors de l'étagère le donne à l'application qui le
    /// reçoit. Les fichiers ne sont ouverts qu'au moment du dépôt — un
    /// fournisseur différé —, jamais au début du geste.
    /// </summary>
    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        string[] paths = e.Items.OfType<ShelfItem>().Select(item => item.FilePath).ToArray();

        if (paths.Length == 0)
        {
            e.Cancel = true;
            return;
        }

        e.Data.RequestedOperation = DataPackageOperation.Copy;
        e.Data.SetDataProvider(StandardDataFormats.StorageItems, async request =>
        {
            DataProviderDeferral deferral = request.GetDeferral();

            try
            {
                var files = new List<IStorageItem>();

                foreach (string path in paths)
                {
                    try
                    {
                        files.Add(await StorageFile.GetFileFromPathAsync(path));
                    }
                    catch (Exception)
                    {
                        // Un fichier déplacé ou supprimé depuis son dépôt est
                        // simplement omis : les autres partent quand même.
                    }
                }

                request.SetData(files);
            }
            finally
            {
                deferral.Complete();
            }
        });
    }
}
