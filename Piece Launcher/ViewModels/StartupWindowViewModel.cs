using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CmlLib.Core;
using Piece_Launcher.Models;
using Piece_Launcher.Services;
using Piece_Launcher.Services.Impl;
using Piece_Launcher.Helpers;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Piece_Launcher.ViewModels
{
    public enum StartupState
    {
        Checking,
        UpdateAvailable,
        Downloading,
        Installing,
        InstallingMinecraft,
        ProfileSetup,
        SigningInProfile,
        SetupFailed,
        Starting,
        UpToDate,
        Failed
    }

    public partial class StartupWindowViewModel : ViewModelBase
    {
        private readonly IUpdateService _updateService;
        private readonly LauncherConfig _config;
        private readonly IVersionService _versionService;
        private readonly IMicrosoftAuthService _microsoftAuthService;
        private readonly bool _checkForUpdates;
        private LauncherUpdateResult? _availableUpdate;
        private bool _isCompleted;
        private bool _setupStarted;

        public event Action? Completed;

        [ObservableProperty]
        private StartupState _state = StartupState.Checking;

        [ObservableProperty]
        private string _titleText = LocalizationHelper.Get("Lang_Startup_CheckingForUpdates");

        [ObservableProperty]
        private string _statusText = "";

        [ObservableProperty]
        private int _downloadProgress;

        [ObservableProperty]
        private double _minecraftInstallProgress;

        [ObservableProperty]
        private string _minecraftInstallProgressText = "";

        [ObservableProperty]
        private string _newProfileName = "Steve";

        [ObservableProperty]
        private bool _isTransitioning;

        [ObservableProperty]
        private double _transitionWidth = 520;

        [ObservableProperty]
        private double _transitionHeight = 320;

        [ObservableProperty]
        private double _transitionOpacity = 1;

        public bool IsCompleted => _isCompleted;
        public bool IsSpinnerVisible => State is StartupState.Checking
            or StartupState.Downloading
            or StartupState.Installing
            or StartupState.InstallingMinecraft
            or StartupState.SigningInProfile;
        public bool IsProgressBarVisible => State is StartupState.Downloading;
        public bool IsMinecraftInstallProgressVisible => State is StartupState.InstallingMinecraft;
        public bool IsUpdateConfirmationVisible => State is StartupState.UpdateAvailable;
        public bool IsProfileSetupVisible => State is StartupState.ProfileSetup;
        public bool IsSetupFailureVisible => State is StartupState.SetupFailed;

        public StartupWindowViewModel(
            IUpdateService updateService,
            bool checkForUpdates,
            LauncherConfig config,
            IVersionService versionService,
            IMicrosoftAuthService microsoftAuthService)
        {
            _updateService = updateService;
            _checkForUpdates = checkForUpdates;
            _config = config;
            _versionService = versionService;
            _microsoftAuthService = microsoftAuthService;
        }

        partial void OnStateChanged(StartupState value)
        {
            OnPropertyChanged(nameof(IsSpinnerVisible));
            OnPropertyChanged(nameof(IsProgressBarVisible));
            OnPropertyChanged(nameof(IsMinecraftInstallProgressVisible));
            OnPropertyChanged(nameof(IsUpdateConfirmationVisible));
            OnPropertyChanged(nameof(IsProfileSetupVisible));
            OnPropertyChanged(nameof(IsSetupFailureVisible));
        }

        public async Task StartAsync()
        {
            if (_checkForUpdates)
            {
                try
                {
                    if (_updateService.IsInstalled)
                    {
                        State = StartupState.Checking;
                        TitleText = LocalizationHelper.Get("Lang_Startup_CheckingForUpdates");
                        StatusText = "";

                        LauncherUpdateResult? update;
                        using var checkTimeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
                        try
                        {
                            update = await _updateService.CheckForUpdatesAsync(checkTimeout.Token)
                                .WaitAsync(checkTimeout.Token);
                        }
                        catch (OperationCanceledException) when (checkTimeout.IsCancellationRequested)
                        {
                            Trace.WriteLine("Launcher update check timed out; continuing startup.");
                            update = null;
                        }

                        if (update is not null)
                        {
                            _availableUpdate = update;
                            State = StartupState.UpdateAvailable;
                            TitleText = LocalizationHelper.Format(
                                "Lang_Startup_UpdateAvailable",
                                update.DisplayVersion);
                            StatusText = LocalizationHelper.Get("Lang_Startup_UpdatePrompt");
                            return;
                        }
                    }
                }
                catch (Exception exception)
                {
                    Trace.WriteLine($"Unable to check for launcher updates: {exception}");
                }
            }

            await ContinueStartupSetupAsync();
        }

        [RelayCommand]
        private async Task InstallUpdate()
        {
            if (State != StartupState.UpdateAvailable || _availableUpdate is null)
                return;

            await InstallUpdateAsync(_availableUpdate);
        }

        [RelayCommand]
        private void ContinueWithoutUpdate()
        {
            if (State != StartupState.UpdateAvailable)
                return;

            _ = ContinueStartupSetupAsync();
        }

        [RelayCommand]
        private async Task RetrySetup()
        {
            if (State != StartupState.SetupFailed)
                return;

            _setupStarted = false;
            await ContinueStartupSetupAsync();
        }

        [RelayCommand]
        private void ContinueWithoutSetup()
        {
            if (State != StartupState.SetupFailed)
                return;

            ShowProfileSetupOrStart();
        }

        [RelayCommand]
        private void SkipProfileSetup()
        {
            if (State != StartupState.ProfileSetup)
                return;

            _config.FirstRunSetupCompleted = true;
            _config.Save();
            StartLauncher();
        }

        [RelayCommand]
        private void AddOfflineProfile()
        {
            if (State != StartupState.ProfileSetup)
                return;

            var name = NewProfileName.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 16)
            {
                StatusText = LocalizationHelper.Get("Lang_Startup_ProfileNameInvalid");
                return;
            }

            var profile = new ProfileEntry { Name = name, Type = ProfileType.Offline };
            _config.Profiles.Add(profile);
            _config.LastSelectedProfileId = profile.Id;
            _config.FirstRunSetupCompleted = true;
            _config.Save();
            StartLauncher();
        }

        [RelayCommand]
        private async Task SignInMicrosoft()
        {
            if (State != StartupState.ProfileSetup)
                return;

            State = StartupState.SigningInProfile;
            TitleText = LocalizationHelper.Get("Lang_Profiles_Connecting");
            StatusText = "";
            try
            {
                var session = await _microsoftAuthService.SignInAsync();
                if (string.IsNullOrWhiteSpace(session.UUID) || string.IsNullOrWhiteSpace(session.Username))
                    throw new InvalidOperationException(LocalizationHelper.Get("Lang_Profiles_IncompleteMicrosoftProfile"));

                var profile = _config.Profiles.FirstOrDefault(candidate =>
                    candidate.Type == ProfileType.Microsoft &&
                    string.Equals(candidate.Id, session.UUID, StringComparison.OrdinalIgnoreCase));
                if (profile is null)
                {
                    profile = new ProfileEntry
                    {
                        Id = session.UUID,
                        Name = session.Username,
                        Type = ProfileType.Microsoft
                    };
                    _config.Profiles.Add(profile);
                }

                _config.LastSelectedProfileId = profile.Id;
                _config.FirstRunSetupCompleted = true;
                _config.Save();
                StartLauncher();
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"Unable to create the initial Microsoft profile: {exception}");
                State = StartupState.ProfileSetup;
                TitleText = LocalizationHelper.Get("Lang_Startup_ProfileSetupTitle");
                StatusText = LocalizationHelper.Format(
                    "Lang_Profiles_SignInFailed",
                    exception.Message);
            }
        }

        private async Task ContinueStartupSetupAsync()
        {
            if (_setupStarted || _isCompleted)
                return;

            _setupStarted = true;
            try
            {
                var installedVersions = await _versionService.GetInstalledVersionNamesAsync();
                var hasInstalledVersion = installedVersions.Count > 0
                    || _config.MinecraftInstances.Any(HasInstalledInstanceVersion);
                if (!hasInstalledVersion)
                {
                    State = StartupState.Checking;
                    TitleText = LocalizationHelper.Get("Lang_Startup_FindingLatestMinecraft");
                    StatusText = "";
                    var latestRelease = (await _versionService.GetAllVersionsAsync())
                        .Where(version => string.Equals(version.Type, "release", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(version => version.ReleaseTime)
                        .FirstOrDefault();
                    if (latestRelease is null)
                        throw new InvalidOperationException(LocalizationHelper.Get("Lang_Startup_NoMinecraftRelease"));

                    State = StartupState.InstallingMinecraft;
                    TitleText = LocalizationHelper.Format(
                        "Lang_Startup_InstallingMinecraft",
                        latestRelease.Name);
                    MinecraftInstallProgress = 0;
                    MinecraftInstallProgressText = "";
                    var progress = new Progress<InstallProgress>(value =>
                    {
                        MinecraftInstallProgress = Math.Clamp(value.Percentage, 0, 100);
                        var stage = LocalizationHelper.GetInstallStage(value.Stage);
                        MinecraftInstallProgressText = string.IsNullOrWhiteSpace(value.CurrentFile)
                            ? stage
                            : $"{stage} · {value.CurrentFile}";
                    });
                    await _versionService.InstallVersionAsync(latestRelease.Name, progress);
                }

                ShowProfileSetupOrStart();
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"Unable to complete initial Minecraft setup: {exception}");
                State = StartupState.SetupFailed;
                TitleText = LocalizationHelper.Get("Lang_Startup_SetupFailed");
                StatusText = exception.Message;
                _setupStarted = false;
            }
        }

        private bool HasInstalledInstanceVersion(MinecraftInstanceEntry instance)
        {
            if (string.IsNullOrWhiteSpace(instance.GameDirectory) ||
                !Directory.Exists(instance.GameDirectory))
                return false;

            var versionsDirectory = Path.Combine(instance.GameDirectory, "versions");
            if (!Directory.Exists(versionsDirectory))
                return false;

            return Directory.GetDirectories(versionsDirectory)
                .Any(path => CmlLibVersionService.IsVersionDownloaded(new DirectoryInfo(path)));
        }

        private void ShowProfileSetupOrStart()
        {
            if (!_config.FirstRunSetupCompleted && _config.Profiles.Count == 0)
            {
                State = StartupState.ProfileSetup;
                TitleText = LocalizationHelper.Get("Lang_Startup_ProfileSetupTitle");
                StatusText = LocalizationHelper.Get("Lang_Startup_ProfileSetupDescription");
                return;
            }

            if (!_config.FirstRunSetupCompleted)
            {
                _config.FirstRunSetupCompleted = true;
                _config.Save();
            }

            StartLauncher();
        }

        private void StartLauncher()
        {
            State = StartupState.Starting;
            TitleText = LocalizationHelper.Get("Lang_Startup_Starting");
            StatusText = "";
            Complete();
        }

        private async Task InstallUpdateAsync(LauncherUpdateResult update)
        {
            State = StartupState.Downloading;
            TitleText = LocalizationHelper.Format("Lang_Startup_DownloadingUpdate", update.DisplayVersion);
            DownloadProgress = 0;
            StatusText = "0%";

            var progress = new Progress<int>(percent =>
            {
                DownloadProgress = percent;
                StatusText = $"{percent}%";
                if (percent >= 100)
                {
                    State = StartupState.Installing;
                    TitleText = LocalizationHelper.Get("Lang_Startup_InstallingUpdate");
                    StatusText = LocalizationHelper.Get("Lang_Startup_Restarting");
                }
            });

            try
            {
                await _updateService.DownloadAndRestartAsync(update, progress);
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"Unable to install launcher update {update.Version}: {exception}");
            }

            // Velopack normally exits and restarts the app here. If it returns or
            // fails, continue startup instead of leaving the launcher closed.
            await ContinueStartupSetupAsync();
        }

        public void ShowStartupFailure(Exception exception)
        {
            State = StartupState.Failed;
            TitleText = LocalizationHelper.Get("Lang_Startup_CouldNotOpen");
            StatusText = exception.Message;
            _isCompleted = true;
            OnPropertyChanged(nameof(IsCompleted));
        }

        private void Complete()
        {
            if (_isCompleted)
                return;

            _isCompleted = true;
            OnPropertyChanged(nameof(IsCompleted));
            Completed?.Invoke();
        }
    }
}
