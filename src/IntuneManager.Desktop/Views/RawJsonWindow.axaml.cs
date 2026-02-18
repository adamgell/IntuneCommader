using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace IntuneManager.Desktop.Views;

public partial class RawJsonWindow : Window
{
    public RawJsonWindow()
    {
        InitializeComponent();
    }

    public RawJsonWindow(string itemTitle, string json) : this()
    {
        Title = $"Raw JSON — {itemTitle}";
        TitleText.Text = itemTitle;
        JsonTextBox.Text = json;
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(JsonTextBox.Text ?? "");
        }
        catch (Exception)
        {
            // Inform the user of the clipboard error.
            var errorWindow = new Window
            {
                Title = "Clipboard Error",
                Width = 400,
                Height = 150,
                Content = new TextBlock
                {
                    Text = "Unable to copy the JSON content to the clipboard. Please try again."
                }
            };

            await errorWindow.ShowDialog(this);
        }
    }
}
