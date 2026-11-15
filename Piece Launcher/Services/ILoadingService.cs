using System;
using System.ComponentModel;

namespace Piece_Launcher.Services
{
    public interface ILoadingService : INotifyPropertyChanged
    {
        bool IsLoading { get; }
        IDisposable Begin();
    }
}
