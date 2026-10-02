using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Alpha6Ops.Identity;

namespace Alpha6Ops.Desktop;

// Isolated UI fixture, invoked only by the explicit desktop diagnostic run. No network identity is issued.
internal static class IdentityWorkspaceSmokeTest
{
    internal static async Task RunAsync(string outputDirectory)
    {
        var design = new AccountWindow(null, preview: true);
        design.Show(); design.UpdateLayout();
        DashboardSmokeTest.Capture(design, Path.Combine(outputDirectory, "identity-design-login.png"));
        design.ContinueButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if (!design.ShowingWorkspaces || design.AirlineCards.Children.Count != 2)
            throw new InvalidOperationException("Login preview must lead to sample workspaces.");
        ((System.Windows.Controls.Button)design.AirlineCards.Children[1]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if (design.SelectedWorkspace.AirlineId is null) throw new InvalidOperationException("Airline card selection failed.");
        DashboardSmokeTest.Capture(design, Path.Combine(outputDirectory, "identity-design-workspaces.png"));
        design.OpenWorkspaceButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if (design.Accepted) throw new InvalidOperationException("Design preview must never authorize a real workspace.");
        design.Width = 960; design.Height = 680; design.UpdateLayout();
        DashboardSmokeTest.Capture(design, Path.Combine(outputDirectory, "identity-design-workspaces-compact.png"));
        design.BackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if (design.ShowingWorkspaces) throw new InvalidOperationException("Preview back navigation failed.");
        DashboardSmokeTest.Capture(design, Path.Combine(outputDirectory, "identity-design-login-compact.png"));
        design.Close();
        var previewOpens = 0;
        var launchPortal = new AccountWindow(null, preview: true, openPilotPreview: () => previewOpens++);
        launchPortal.Show();
        launchPortal.ContinueButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        ((System.Windows.Controls.Button)launchPortal.AirlineCards.Children[0]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        launchPortal.OpenWorkspaceButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if (previewOpens != 0 || launchPortal.Accepted) throw new InvalidOperationException("Launcher must not authorize or open a sample airline.");
        launchPortal.PersonalCard.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        launchPortal.OpenWorkspaceButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if (previewOpens != 1 || launchPortal.Accepted || launchPortal.IsVisible)
            throw new InvalidOperationException("Launcher must close login and enter only the isolated pilot preview without authorization.");
        var root = Path.Combine(outputDirectory, "identity-ui");
        var config = new IdentityConfiguration("https://test.invalid", "diagnostic-client", "https://api.test.invalid", "https://api.test.invalid/", "https://test.invalid/");
        var account = new DesktopAccountSession(config, root);
        var welcome = new AccountWindow(account);
        welcome.Show(); welcome.UpdateLayout();
        if (!OpsUi.UsesWindowTheme(welcome)) throw new InvalidOperationException("Account window must use shared Alpha 6 chrome.");
        DashboardSmokeTest.Capture(welcome, Path.Combine(outputDirectory, "identity-welcome.png"));
        welcome.Close();

        var now = DateTimeOffset.UtcNow;
        var bootstrap = new BootstrapResponse(new(Guid.NewGuid(), "Identity Test Pilot", "pilot@example.invalid", true, AccountStatus.Active),
            new(PersonalPlan.Free, "active", null), Capabilities.Personal,
            [new(Guid.NewGuid(), "test-air", "Test Airline", "TEST", AirlinePlan.Community, "active", MembershipStatus.Active,
                [AirlineRole.Pilot], false, [Capabilities.AirlineRead])], WorkspaceSelection.Personal, now, now.AddDays(30));
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(config))));
        var saved = new SavedAccountSession(key, null, bootstrap, now, now, WorkspaceSelection.Personal);
        Directory.CreateDirectory(Path.Combine(root, "Identity"));
        File.WriteAllBytes(Path.Combine(root, "Identity", "session.bin"), ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(saved), null, DataProtectionScope.CurrentUser));
        if (!await account.RestoreAsync(default)) throw new InvalidOperationException("Diagnostic offline session could not be restored.");
        var chooser = new AccountWindow(account);
        chooser.Show(); chooser.UpdateLayout();
        if (chooser.AirlineCards.Children.OfType<System.Windows.Controls.Button>().Any(button => button.IsEnabled))
            throw new InvalidOperationException("Offline personal workspace must not enable another airline card.");
        DashboardSmokeTest.Capture(chooser, Path.Combine(outputDirectory, "identity-workspaces.png"));
        chooser.Close();

        var checks = 9 + await AccountFlowSmokeTest.RunAsync(outputDirectory);
        void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); checks++; }
        var dispatcher = new WpfFlightRuntimeDispatcher(Application.Current.Dispatcher);
        FlightSessionCoordinator Session(string directory, AccountFlightIdentity? identity = null) => new(directory, true, dispatcher, null, identity);
        var identity = account.FlightIdentity;
        Check(identity == new AccountFlightIdentity(bootstrap.Account.Id, bootstrap.Account.DisplayName, null), "Account flight identity must name the pilot and personal workspace.");
        Check(account.DataDirectory.StartsWith(Path.Combine(root, "accounts"), StringComparison.OrdinalIgnoreCase) && account.DataDirectory.EndsWith("personal", StringComparison.OrdinalIgnoreCase), "Personal workspace data must live under the account directory.");
        var session = Session(account.DataDirectory, identity);
        var pilot = new MainWindow(session, root, account);
        try
        {
            pilot.Show(); pilot.Width = 1440; pilot.Height = 960; pilot.UpdateLayout();
            Check(pilot.PilotNameBox.IsReadOnly && pilot.PilotNameBox.Text == bootstrap.Account.DisplayName, "Authenticated pilot identity must be read-only.");
            Check(pilot.IsPilotWorkspace && pilot.Shell.AccountButton.IsVisible && pilot.Shell.AccountLabel.Text.Contains("Pilot", StringComparison.OrdinalIgnoreCase), "Signed-in shell must present the account workspace.");
            Check(!pilot.Shell.Navigation.Allows(ShellSection.Crew) && !pilot.Shell.Navigation.Allows(ShellSection.Fleet) && !pilot.Shell.Navigation.Allows(ShellSection.Occ)
                && pilot.Shell.Navigation.Allows(ShellSection.Flights) && pilot.Shell.Navigation.Allows(ShellSection.Dispatch) && pilot.Shell.Navigation.Allows(ShellSection.Logbook), "Pilot tier must hide airline destinations only.");
            Check(pilot.Shell.PrimaryNavigation.Items.Cast<ShellDestination>().All(d => PilotFeatureProfile.Allows(d.Section)) && pilot.Shell.Navigation.Search("Fleet").Count == 0, "Hidden destinations must not appear in navigation or search.");
            pilot.Shell.Navigation.Request(ShellSection.Crew);
            Check(pilot.Shell.Navigation.Current == ShellSection.Operations, "A hidden destination request must be ignored.");
            Check(pilot.FlightHistory?.ReadRecentFlights().Count == 0, "New account must not inherit another account's history.");
            Check(!pilot.AdvancedButton.IsVisible, "Pilot tier must not expose advanced rotation controls.");
            DashboardSmokeTest.Capture(pilot, Path.Combine(outputDirectory, "identity-pilot-home.png"));
            pilot.ShowPilotLogbook(); pilot.UpdateLayout();
            Check(pilot.PilotLogbookView.IsVisible && pilot.PilotLogbookView.VisibleFlightCount == 0, "Logbook must show this account's empty history.");
            DashboardSmokeTest.Capture(pilot, Path.Combine(outputDirectory, "pilot-logbook-empty.png"));
            pilot.ShowDispatch(); pilot.UpdateLayout();
            Check(pilot.DispatchView.IsVisible && !pilot.DispatchView.HasActiveRelease, "Dispatch must open with an honest empty assignment.");
            var id = session.BeginFlightHistory("diagnostic", "Isolated UI fixture", "A320", "KORD", "KMSP", "TEST 101");
            Check(id is not null && pilot.FlightHistory!.ReadFlightEvents(id).Any(e => e.Kind == "account_identity" && e.DetailJson.Contains(bootstrap.Account.Id.ToString())), "Each flight must record the account that flew it.");
            session.EndFlightHistory(id, "Complete");
            var plan = new ActiveFlightPlan("TEST 101", "N101", "KORD", "KMSP", now, now.AddHours(2), Route: "TEST ROUTE", AircraftType: "A320");
            session.AcceptAssignment(plan);
        }
        finally { pilot.CloseForWorkspaceChange(); session.Dispose(); }
        session = Session(account.DataDirectory, identity);
        var restored = new MainWindow(session, root, account);
        try
        {
            restored.Show(); restored.UpdateLayout();
            Check(restored.ActivePlanForTest?.FlightNumber == "TEST 101" && ActiveFlightPlanStore.Load(account.DataDirectory) is not null, "The workspace's assignment must survive a new application session.");
            restored.OpenTools();
            restored.ClearFlightButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(restored.ActivePlanForTest is null && ActiveFlightPlanStore.Load(account.DataDirectory) is null, "Clearing a plan must clear only the assignment.");
            restored.ShowPilotLogbook();
            Check(restored.PilotLogbookView.VisibleFlightCount == 1, "Clearing an assignment must preserve recorded history.");
        }
        finally { restored.CloseForWorkspaceChange(); session.Dispose(); }
        var otherRoot = Path.Combine(root, "other-workspace");
        using (var other = Session(otherRoot))
            Check(other.Snapshot.Assignment is null && other.History?.ReadRecentFlights().Count == 0, "Another workspace must not see this workspace's flights.");
        // App owns one application session per workspace and replaces it only when the workspace changes.
        var first = App.GetFlightSession(null, account);
        Check(ReferenceEquals(first, App.GetFlightSession(null, account)) && first.DataDirectory == account.DataDirectory, "The same workspace must reuse its application session.");
        var second = App.GetFlightSession(null, null);
        var replaced = false;
        try { first.AcceptAssignment(new ActiveFlightPlan("TEST 102", "N102", "KORD", "KMSP", now, now.AddHours(2))); } catch (ObjectDisposedException) { replaced = true; }
        Check(replaced && !ReferenceEquals(first, second) && second.DataDirectory != account.DataDirectory, "A workspace change must dispose the previous application session.");
        var previewRoot = Path.Combine(root, "pilot-preview");
        var previewSession = Session(previewRoot);
        var preview = new MainWindow(previewSession, previewRoot, null, pilotPreview: true);
        try
        {
            Check(preview.IsPilotWorkspace && preview.FlightHistory?.ReadRecentFlights().Count == 0 && preview.ActivePlanForTest is null && !preview.Shell.Navigation.Allows(ShellSection.Fleet), "Preview must use the restricted shell and isolated empty data.");
        }
        finally { preview.CloseForWorkspaceChange(); previewSession.Dispose(); }
        File.WriteAllText(Path.Combine(outputDirectory, "identity-ui-smoke.json"), JsonSerializer.Serialize(new { passed = true, checks }));
    }
}
