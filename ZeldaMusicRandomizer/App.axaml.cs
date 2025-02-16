using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using ZeldaMusicRandomizer.ViewModels;
using ZeldaMusicRandomizer.Views;

namespace ZeldaMusicRandomizer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            desktop.MainWindow.DataContext = new MainViewModel(desktop.MainWindow, false);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView();
            singleViewPlatform.MainView.DataContext = new MainViewModel(TopLevel.GetTopLevel(singleViewPlatform.MainView)!, true);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
