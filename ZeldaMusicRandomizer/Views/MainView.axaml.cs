using Avalonia.Controls;
using Avalonia.Input;

namespace ZeldaMusicRandomizer.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();

        RepositoryUrlButton.Tapped += RepositoryUrlButton_Tapped;
    }

    private async void RepositoryUrlButton_Tapped(object? sender, TappedEventArgs e)
    {
        var launcher = TopLevel.GetTopLevel(this)!.Launcher;
        await launcher.LaunchUriAsync(
            new((string)RepositoryUrlButton.Tag!));
    }
}
