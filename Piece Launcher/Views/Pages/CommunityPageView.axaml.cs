using Avalonia.Markup.Xaml;
using Avalonia.Controls;

namespace Piece_Launcher.Views.Pages
{
    public partial class CommunityPageView : UserControl
    {
        public CommunityPageView()
        {
            InitializeComponent();
        }
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
