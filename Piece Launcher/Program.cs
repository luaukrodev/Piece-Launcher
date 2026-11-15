using Avalonia;
using System;
using System.Threading;
using Velopack;

namespace Piece_Launcher
{
    internal sealed class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            VelopackApp.Build().Run();

            using var singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                name: @"Local\PieceLauncher.SingleInstance",
                createdNew: out var isFirstInstance);
            if (!isFirstInstance)
                return;

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace();
    }
}