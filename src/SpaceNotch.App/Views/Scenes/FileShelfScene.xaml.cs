using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpaceNotch.Core.Activities;
using SpaceNotch.Features.FileShelf;
using SpaceNotch_App.Views;

namespace SpaceNotch_App.Views.Scenes;

public sealed partial class FileShelfScene : UserControl, IIslandSceneView
{
    public FileShelfScene()
    {
        InitializeComponent();
    }

    public FrameworkElement Root => this;

    public void Apply(IslandActivity activity) => CountText.Text = activity.Title;

    public void UpdateItems(IReadOnlyList<ShelfItem> items)
    {
        FilesListView.ItemsSource = items;
        CountText.Text = $"{items.Count} fichier{(items.Count > 1 ? "s" : "")}";
    }
}
