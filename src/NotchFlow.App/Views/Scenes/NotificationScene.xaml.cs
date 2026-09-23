using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NotchFlow.Core.Activities;
using NotchFlow_App.Views;

namespace NotchFlow_App.Views.Scenes;

public sealed partial class NotificationScene : UserControl, IIslandSceneView
{
    public event Action? DismissRequested;

    public NotificationScene()
    {
        InitializeComponent();
    }

    public FrameworkElement Root => this;

    public void Apply(IslandActivity activity)
    {
        if (activity.Payload is ValueTuple<string, string, string> notification)
        {
            UpdateNotification(notification.Item1, notification.Item2, notification.Item3);
        }
    }

    public void UpdateNotification(string appName, string title, string body)
    {
        AppSourceText.Text = appName;
        TitleText.Text = title;
        BodyText.Text = body;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        DismissRequested?.Invoke();
    }
}
