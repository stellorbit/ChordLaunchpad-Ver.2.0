using System;
using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChordLaunchpad;

public sealed partial class HelpDialog : ContentDialog
{
    public HelpDialog()
    {
        InitializeComponent();
        if (HelpNav.MenuItems.Count > 0)
        {
            HelpNav.SelectedItem = HelpNav.MenuItems[0];
        }
    }

    private void HelpNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            InputPanel.Visibility = tag == "Input" ? Visibility.Visible : Visibility.Collapsed;
            SuffixPanel.Visibility = tag == "Suffix" ? Visibility.Visible : Visibility.Collapsed;
            ShortcutsPanel.Visibility = tag == "Shortcuts" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void OpenDocFolder_Click(object sender, RoutedEventArgs e)
    {
        const string docUrl = "https://github.com/stellorbit/ChordLaunchpad-Ver.2.0#readme";
        try
        {
            if (Uri.TryCreate(docUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            {
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
        }
        catch { }
    }
}
