namespace Piece_Launcher.Services
{
    public interface ILanguageService
    {
        string CurrentLanguage { get; }
        void ApplyLanguage(string languageName);
    }
}
