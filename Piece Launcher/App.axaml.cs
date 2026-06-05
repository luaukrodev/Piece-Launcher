using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Piece_Launcher.ViewModels;
using Piece_Launcher.Views;
using DotNetEnv;
using System.Linq;

namespace Piece_Launcher
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            Env.Load();
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(),
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}