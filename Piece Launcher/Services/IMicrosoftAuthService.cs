using CmlLib.Core.Auth;
using System.Threading.Tasks;

namespace Piece_Launcher.Services
{
    public interface IMicrosoftAuthService
    {
        Task<MSession> SignInAsync();
        Task<MSession> AuthenticateProfileAsync(string profileId);
    }
}
