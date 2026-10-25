using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Auth.Microsoft.Sessions;
using Microsoft.Identity.Client;
using Piece_Launcher.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using XboxAuthNet.Game.Msal;

namespace Piece_Launcher.Services.Impl
{
    public sealed class MicrosoftAuthService : IMicrosoftAuthService
    {
        private readonly JELoginHandler _loginHandler = JELoginHandlerBuilder.BuildDefault();
        private readonly IPublicClientApplication _msalApp =
            MsalClientHelper.BuildApplicationWithCache(LauncherDefaults.MicrosoftClientId).GetAwaiter().GetResult();

        public async Task<MSession> SignInAsync()
        {
            var authenticator = _loginHandler.CreateAuthenticatorWithNewAccount();
            authenticator.AddMsalOAuth(_msalApp, msal => msal.SystemBrowser());
            authenticator.AddXboxAuthForJE(xbox => xbox.Basic());
            authenticator.AddForceJEAuthenticator();
            return await authenticator.ExecuteForLauncherAsync();
        }

        public async Task<MSession> AuthenticateProfileAsync(string profileId)
        {
            var account = _loginHandler.AccountManager.GetAccounts()
                .OfType<JEGameAccount>()
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Profile?.UUID, profileId, StringComparison.OrdinalIgnoreCase));

            if (account == null)
                throw new InvalidOperationException("The Microsoft account session is unavailable. Sign in again.");

            var authenticator = _loginHandler.CreateAuthenticator(account, default);
            authenticator.AddMsalOAuth(_msalApp, msal => msal.Silent());
            authenticator.AddXboxAuthForJE(xbox => xbox.Basic());
            authenticator.AddJEAuthenticator();
            return await authenticator.ExecuteForLauncherAsync();
        }
    }
}
