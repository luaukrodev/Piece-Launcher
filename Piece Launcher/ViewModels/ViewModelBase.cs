using CommunityToolkit.Mvvm.ComponentModel;

namespace Piece_Launcher.ViewModels
{
    public abstract partial class ViewModelBase : ObservableObject
    {
        [ObservableProperty]
        private bool _isActive;

        partial void OnIsActiveChanged(bool value)
        {
            if (value)
            {
                OnActivated();
            }
            else
            {
                OnDeactivated();
            }
        }

        protected virtual void OnActivated() { }
        protected virtual void OnDeactivated() { }
    }
}
