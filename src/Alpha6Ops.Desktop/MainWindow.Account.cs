using System;
using System.Threading.Tasks;
using System.Windows;
using Alpha6Ops.Core;
using Alpha6Ops.Identity;

namespace Alpha6Ops.Desktop;

// Account presentation over the application flight session. The coordinator for this window was
// created for this account workspace (App.GetFlightSession); a workspace change replaces both.
public partial class MainWindow
{
    private readonly DesktopAccountSession? account;
    private readonly bool pilotPreview;
    internal bool IsPilotWorkspace => pilotPreview || account?.Bootstrap is not null;
    private bool changingAccount;
    private bool FlightInProgress => running || flightState.ConnectionActive || flightState.Phase is not null and not FlightPhase.Complete;

    private bool MayStartAccountFlight()
    {
        if (account is null || account.MayStartNewFlight(DateTimeOffset.UtcNow)) return true;
        OpsNoticeWindow.Show(this, "Sign-in required", "Your local authorization has expired. Open Account and reconnect before starting a new flight. Recorded flights are preserved.");
        return false;
    }
    // Best effort: the local cache already has the value; the profile just makes it follow the account.
    private async Task RememberSimBriefAsync(string username)
    {
        if (account?.Bootstrap is null || account.IsOffline || username.Length < 2 || account.Profile.SimBriefUsername == username) return;
        var p = account.Profile;
        try { await account.UpdateProfileAsync(new(username, p.Callsign, p.HomeBaseIcao, p.WeightUnit, p.AltitudeUnit, p.LandingDistanceUnit, p.PreferredWorkspace, p.TimeZone, p.AvatarInitials), lifetime.Token); }
        catch (Exception error) when (error is AccountSessionException or System.Net.Http.HttpRequestException or OperationCanceledException) { }
    }

    private async Task SyncUnitsToProfileAsync(GeneralSettings settings)
    {
        if (account?.Bootstrap is null || account.IsOffline) return;
        var p = account.Profile;
        if (p.WeightUnit == settings.WeightUnit && p.AltitudeUnit == settings.AltitudeUnit && p.LandingDistanceUnit == settings.LandingDistanceUnit) return;
        try { await account.UpdateProfileAsync(new(p.SimBriefUsername, p.Callsign, p.HomeBaseIcao, settings.WeightUnit, settings.AltitudeUnit, settings.LandingDistanceUnit, p.PreferredWorkspace, p.TimeZone, p.AvatarInitials), lifetime.Token); }
        catch (Exception error) when (error is AccountSessionException or System.Net.Http.HttpRequestException or OperationCanceledException) { }
    }

    private void InitializeAccountWorkspace()
    {
        if (diagnosticMode && account is null && !pilotPreview) return;
        Shell.AccountRequested += Account_Click;
        Shell.PresentAccount(pilotPreview ? "Pilot · isolated preview" : account is null ? "Account · local preview" : account.WorkspaceName + (account.IsOffline ? " · offline" : ""),
            account?.Bootstrap is { } signedIn ? $"{signedIn.Account.DisplayName} · {account.WorkspaceName}\nChange account or workspace" : "Sign in or change workspace");
        if (account?.Bootstrap is null && !pilotPreview) return;
        var bootstrap = account?.Bootstrap;
        var workspace = account?.WorkspaceName ?? "Flying as a Pilot";
        if (bootstrap is not null) { PilotNameBox.Text = bootstrap.Account.DisplayName; PilotNameBox.IsReadOnly = true; }
        Title += " · " + workspace;
        FooterBrand.Text = "SIERRA 4  /  PILOT WORKSPACE";
        // The pilot tier is a feature profile of the same shell, not a different visual shell.
        Shell.RestrictNavigation(PilotFeatureProfile.Allows);
        AdvancedButton.Visibility = Visibility.Collapsed;
        SetAdvanced(false);
        if (account?.Profile.SimBriefUsername is { Length: > 0 } simBrief) DispatchView.UseSimBriefUsername(simBrief);
        DispatchView.SimBriefUsernameUsed += async (_, username) => await RememberSimBriefAsync(username);
    }

    private async void Account_Click(object? sender, EventArgs e)
    {
        if (changingAccount) return;
        if (FlightInProgress)
        { OpsNoticeWindow.Show(this, "Flight in progress", "Finish the current flight and disconnect before changing account or workspace. Your recorder stays attached to its original pilot and workspace."); return; }
        changingAccount = true;
        IsEnabled = false;
        try
        {
            var previous = account?.Workspace;
            var previousUser = account?.Bootstrap?.Account.Id;
            var previousBootstrap = account?.Bootstrap;
            var previouslyOffline = account?.IsOffline;
            if (account is not null)
            {
                // Refresh before exposing a workspace selector. Existing recording is already protected above.
                try { await account.RestoreAsync(lifetime.Token); }
                catch (Exception) { /* The selector shows sign-in or retained local access; never display raw provider errors. */ }
            }
            if (FlightInProgress) return;
            var chooser = new AccountWindow(account) { Owner = this }; chooser.ShowDialog();
            if (chooser.SignedOut)
            {
                Hide();
                var login = new AccountWindow(account); login.ShowDialog();
                if (!login.Accepted) { ExitApplication(); return; }
            }
            if (account is not null && account.Bootstrap is null) { ExitApplication(); return; }
            if (chooser.SignedOut || account?.Workspace != previous || account?.Bootstrap?.Account.Id != previousUser
                || !ReferenceEquals(account?.Bootstrap, previousBootstrap) || account?.IsOffline != previouslyOffline)
            {
                // Release this window's presentation before App replaces the flight session it observes.
                CloseForWorkspaceChange();
                var next = new MainWindow(App.GetFlightSession(null, account), null, account);
                Application.Current.MainWindow = next; next.Show();
            }
        }
        finally { changingAccount = false; IsEnabled = true; }
    }
    internal void CloseForWorkspaceChange()
    {
        exiting = true;
        var savedBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        UserPreferencesStore.Save(new UserPreferences(savedBounds.Width, savedBounds.Height, WindowState == WindowState.Maximized, false, SelectedFixture, PilotNameBox.Text));
        DisposePresentation();
        Close();
    }
}
