using Avalonia.Markup.Xaml;
using Avalonia.Controls;

namespace Piece_Launcher.Views.Pages
{
    public partial class SettingsPageView : UserControl
    {
        public SettingsPageView()
        {
            InitializeComponent();
        }
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
