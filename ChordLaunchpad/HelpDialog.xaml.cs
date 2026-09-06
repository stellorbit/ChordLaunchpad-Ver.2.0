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

    private void OpenDocFolder_Click(object sender, RoutedEventArgs e)
    {
        const string docPath = @"F:\Codex\chord-draft\docs";
        try
        {
            if (Directory.Exists(docPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = docPath,
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }
}
