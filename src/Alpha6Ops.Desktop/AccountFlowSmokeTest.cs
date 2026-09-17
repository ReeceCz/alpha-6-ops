using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Alpha6Ops.Identity;

namespace Alpha6Ops.Desktop;

// Explicit diagnostic fixture: drives real controls with isolated provider/API responses.
internal static class AccountFlowSmokeTest
{
    internal static async Task<int> RunAsync(string outputDirectory)
    {
        var checks = 0;
        void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); checks++; }
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        static async Task IdleAsync(AccountWindow window)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!window.LoginPage.IsEnabled) await Task.Delay(10, timeout.Token);
            window.UpdateLayout();
        }
        var now = DateTimeOffset.UtcNow;
        var bootstrap = new BootstrapResponse(new(Guid.NewGuid(), "New Pilot", "pilot@example.invalid", false, AccountStatus.Active),
            new(PersonalPlan.Free, "active", null), Capabilities.Personal, [], WorkspaceSelection.Personal, now, now.AddDays(30));
        var config = new IdentityConfiguration("https://test.invalid", "test-client", "https://api.test.invalid", "https://api.test.invalid/", "https://test.invalid/");
        var root = Path.Combine(outputDirectory, "account-flow");
        var loginCalls = 0;
        var waitForCancel = true;
        var revoked = false;
        var signup = false;
        var stepUps = 0;
        var mfaPending = false;
        var creates = 0;
        static AirlineWorkspace Owned(string slug, string name, string callsign) => new(Guid.NewGuid(), slug, name, callsign, AirlinePlan.Community, "active", MembershipStatus.Active,
            [AirlineRole.Pilot, AirlineRole.Owner], true, Capabilities.ForMembership(MembershipStatus.Active, [AirlineRole.Pilot, AirlineRole.Owner]));
        var createdAirline = Owned("created-airline", "Created Airline", "CRT");
        var joinedAirline = new AirlineWorkspace(Guid.NewGuid(), "friends", "Friends Virtual", "FRV", AirlinePlan.Pro, "active", MembershipStatus.Active, [AirlineRole.Pilot], false, Capabilities.ForMembership(MembershipStatus.Active, [AirlineRole.Pilot]));
        var session = new DesktopAccountSession(config, root, () => new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/oauth/token")
                return revoked ? new(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = "invalid_grant" }) }
                    : new(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = "test-access", refresh_token = "test-refresh", token_type = "Bearer", expires_in = 300 }) };
            if (path == "/api/v1/me/bootstrap") return new(HttpStatusCode.OK) { Content = JsonContent.Create(bootstrap) };
            if (path == "/api/v1/me/profile" && request.Method == HttpMethod.Put)
            {
                var body = request.Content!.ReadFromJsonAsync<UpdateProfileRequest>().GetAwaiter().GetResult()!;
                var profile = new UserProfile(body.SimBriefUsername, body.Callsign.ToUpperInvariant(), body.HomeBaseIcao.ToUpperInvariant(), body.WeightUnit, body.AltitudeUnit, body.LandingDistanceUnit, body.PreferredWorkspace, body.TimeZone, body.AvatarInitials.ToUpperInvariant(), now, "0.16.0", now);
                bootstrap = bootstrap with { Profile = profile };
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(profile) };
            }
            if (path == "/api/v1/me/plan")
            {
                bootstrap = bootstrap with { PersonalEntitlement = new(PersonalPlan.Premium, "complimentary", null) };
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(bootstrap.PersonalEntitlement) };
            }
            if (path == "/api/v1/virtual-airlines" && request.Method == HttpMethod.Post)
            {
                creates++;
                if (mfaPending) return new(HttpStatusCode.Forbidden) { Content = JsonContent.Create(new { title = "Confirm your identity with a passkey or MFA to continue.", status = 403, code = "mfa_required" }) };
                bootstrap = bootstrap with { Airlines = [.. bootstrap.Airlines, createdAirline] };
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(createdAirline) };
            }
            if (path == "/api/v1/invitations/accept")
            {
                bootstrap = bootstrap with { Airlines = [.. bootstrap.Airlines, joinedAirline] };
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(joinedAirline) };
            }
            if (path.EndsWith("/invitations") && request.Method == HttpMethod.Post)
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new IssuedInvitation(new InvitationResponse(Guid.NewGuid(), "CREW@EXAMPLE.INVALID", [AirlineRole.Pilot], now.AddDays(7), "pending"), "raw-invite-code")) };
            if (path.EndsWith("/invitations"))
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { new InvitationResponse(Guid.NewGuid(), "CREW@EXAMPLE.INVALID", [AirlineRole.Pilot], now.AddDays(7), "pending") }) };
            if (path.EndsWith("/members"))
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { new MemberResponse(Guid.NewGuid(), bootstrap.Account.Id, "New Pilot", "pilot@example.invalid", MembershipStatus.Active, [AirlineRole.Pilot, AirlineRole.Owner]), new MemberResponse(Guid.NewGuid(), Guid.NewGuid(), "Crew Member", "crew@example.invalid", MembershipStatus.Active, [AirlineRole.Pilot]) }) };
            return new(HttpStatusCode.NoContent);
        }), async (request, token) =>
        {
            loginCalls++;
            if (request.FrontChannelExtraParameters.Any(p => p.Key == "acr_values" && p.Value == DesktopAccountSession.MultiFactorPolicy)) stepUps++;
            signup = request.FrontChannelExtraParameters.Any(p => p.Key == "screen_hint" && p.Value == "signup");
            if (waitForCancel) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new BrowserLoginResult("test-access", "test-refresh");
        });
        var window = new AccountWindow(session);
        try
        {
            window.Show();
            window.EmailInput.Text = "invalid-email";
            Click(window.ContinueButton);
            Check(loginCalls == 0 && window.LoginStatus.Text.Contains("valid email"), "Invalid email must be rejected before browser launch.");
            window.EmailInput.Text = "pilot@example.invalid";
            Click(window.ContinueButton);
            Check(!window.LoginPage.IsEnabled && window.CancelSignInButton.IsVisible && window.CancelSignInButton.IsEnabled,
                "Sign-in must disable duplicate submissions while keeping Cancel usable.");
            window.UpdateLayout();
            DashboardSmokeTest.Capture(window, Path.Combine(outputDirectory, "identity-login-pending.png"));
            Click(window.ContinueButton);
            Check(loginCalls == 1, "Repeated sign-in clicks must not start concurrent login flows.");
            Click(window.CancelSignInButton);
            await IdleAsync(window);
            Check(!window.ShowingWorkspaces && session.Bootstrap is null && window.LoginStatus.Text.Contains("canceled") && !window.CancelSignInButton.IsVisible,
                "Canceled sign-in must restore a usable login screen without a session.");
            waitForCancel = false;
            window.EmailInput.Text = "";
            Click(window.RegisterButton);
            await IdleAsync(window);
            Check(signup && window.ShowingWorkspaces && session.Bootstrap is not null && !window.Accepted,
                "Registration must establish a desktop session and wait for explicit workspace selection.");
            DashboardSmokeTest.Capture(window, Path.Combine(outputDirectory, "identity-new-account.png"));
            Check(window.EmptyAirlines.IsVisible && window.ReconnectButton.IsVisible && window.ReconnectLabel.Text == "Refresh",
                $"New accounts must show personal workspace and membership refresh (empty: {window.EmptyAirlines.IsVisible}, refresh: {window.ReconnectButton.IsVisible}, label: {window.ReconnectLabel.Text}).");
            bootstrap = bootstrap with { Airlines = [new(Guid.NewGuid(), "new-air", "New Airline", "NEW", AirlinePlan.Community, "active", MembershipStatus.Active, [AirlineRole.Pilot], false, [])] };
            Click(window.ReconnectButton);
            await IdleAsync(window);
            Check(window.AirlineCards.Children.Count == 1 && loginCalls == 2, "Refresh must load new memberships without another browser sign-in.");
            Check(!window.JoinButton.IsEnabled && !window.CreateButton.IsEnabled && window.CrewHintText.Text.Contains("Verify your email"),
                "Unverified accounts see why airline actions are unavailable.");
            bootstrap = bootstrap with { Account = bootstrap.Account with { EmailVerified = true } };
            Click(window.ReconnectButton);
            await IdleAsync(window);
            Check(window.JoinButton.IsEnabled && window.CreateButton.IsEnabled, "Verified accounts can join and create airlines natively.");

            var profileDialog = new ProfileWindow(window, session, root);
            profileDialog.Show();
            profileDialog.Input<TextBox>("SimBrief username").Text = "reece74";
            profileDialog.Input<TextBox>("Avatar initials").Text = "rc";
            profileDialog.UpdateLayout();
            DashboardSmokeTest.Capture(profileDialog, Path.Combine(outputDirectory, "identity-profile.png"));
            await profileDialog.SubmitAsync();
            Check(profileDialog.Saved?.SimBriefUsername == "reece74" && session.Profile.AvatarInitials == "RC" && !profileDialog.IsVisible,
                $"Profile dialog saves to the account and caches locally ({profileDialog.StatusText}).");
            window.ShowWorkspaces();
            Check(window.AccountInitialsText.Text == "RC", "Avatar uses the profile initials.");

            var planDialog = new PlanSelectionWindow(window, session);
            planDialog.Show(); planDialog.Choose("Premium"); planDialog.UpdateLayout();
            DashboardSmokeTest.Capture(planDialog, Path.Combine(outputDirectory, "identity-plan.png"));
            await planDialog.SubmitAsync();
            window.ShowWorkspaces();
            Check(planDialog.Changed && window.PlanText.Text == "PREMIUM" && session.Bootstrap!.PersonalEntitlement.Status == "complimentary",
                $"Choosing a level applies a complimentary plan and refreshes the badge ({planDialog.StatusText}).");

            mfaPending = true; creates = 0;
            var confirmations = 0; var loginsBefore = loginCalls;
            var createDialog = new CreateAirlineWindow(window, session) { ConfirmOverride = () => { confirmations++; mfaPending = false; return Task.FromResult(true); } };
            createDialog.Show();
            createDialog.Input<TextBox>("Airline name").Text = "Created Airline";
            createDialog.Input<TextBox>("Callsign").Text = "crt";
            Check(createDialog.Input<TextBox>("Airline address").Text == "created-airline", "Airline address is derived from the name.");
            createDialog.UpdateLayout();
            DashboardSmokeTest.Capture(createDialog, Path.Combine(outputDirectory, "identity-create-airline.png"));
            await createDialog.SubmitAsync();
            Check(createDialog.Created?.Name == "Created Airline" && confirmations == 1 && stepUps == 1 && loginCalls == loginsBefore + 1 && creates == 2,
                $"Creating an airline prompts once, steps up in the browser, and retries exactly once ({createDialog.StatusText}).");
            window.ShowWorkspaces();
            Check(window.AirlineCards.Children.Count == 2, "The new airline appears without another sign-in.");

            var joinDialog = new JoinAirlineWindow(window, session);
            joinDialog.Show();
            joinDialog.Input<TextBox>("Invitation code").Text = "friend-code";
            await joinDialog.SubmitAsync();
            window.ShowWorkspaces();
            Check(joinDialog.Joined?.Name == "Friends Virtual" && window.AirlineCards.Children.Count == 3, $"Joining with a code adds the airline ({joinDialog.StatusText}).");

            var inviteDialog = new InviteMemberWindow(window, session, createdAirline) { ConfirmOverride = () => Task.FromResult(true) };
            inviteDialog.Show();
            inviteDialog.Input<TextBox>("Member email").Text = "crew@example.invalid";
            await inviteDialog.SubmitAsync();
            inviteDialog.UpdateLayout();
            Check(inviteDialog.Issued?.Token == "raw-invite-code" && inviteDialog.IsVisible && inviteDialog.Inputs<TextBox>().Any(t => t.Text == "raw-invite-code" && t.IsReadOnly),
                $"An issued invitation shows its code once for sharing ({inviteDialog.StatusText}).");
            DashboardSmokeTest.Capture(inviteDialog, Path.Combine(outputDirectory, "identity-invite-issued.png"));
            inviteDialog.Close();

            var membersDialog = new AirlineMembersWindow(window, session, createdAirline) { ConfirmOverride = () => Task.FromResult(true) };
            membersDialog.Show();
            await membersDialog.ReloadAsync();
            membersDialog.UpdateLayout();
            Check(membersDialog.Inputs<TextBlock>().Any(t => t.Text == "Crew Member") && membersDialog.Inputs<TextBlock>().Any(t => t.Text == "crew@example.invalid"),
                $"The roster lists members and pending invitations ({membersDialog.StatusText}).");
            DashboardSmokeTest.Capture(membersDialog, Path.Combine(outputDirectory, "identity-members.png"));
            membersDialog.Close();
            revoked = true;
            Click(window.ReconnectButton);
            await IdleAsync(window);
            Check(!window.ShowingWorkspaces && session.Bootstrap is null && window.LoginStatus.Text.Contains("expired"),
                "Revoked credentials must return the selector to sign-in.");
            revoked = false;
            Click(window.OtherLoginButton);
            await IdleAsync(window);
            Check(window.ShowingWorkspaces && !signup, "Other sign-in options must recover from revocation using normal login.");
            Click(window.OpenWorkspaceButton);
            await IdleAsync(window);
            Check(window.Accepted && !window.IsVisible, "Open workspace must persist selection and complete the account dialog.");
        }
        finally { if (window.IsVisible) window.Close(); }

        // "Open at sign-in" decides what a restored session does before the pilot sees anything.
        static async Task WaitAsync(Func<bool> condition, string message)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!condition()) { if (timeout.IsCancellationRequested) throw new InvalidOperationException(message); await Task.Delay(10); }
        }
        async Task<AccountWindow> RestoreAsync(string preference, Func<AccountWindow, bool> settled)
        {
            bootstrap = bootstrap with { Profile = (bootstrap.Profile ?? UserProfile.Default) with { PreferredWorkspace = preference } };
            var restored = new AccountWindow(session, restore: true);
            restored.Show();
            await WaitAsync(() => settled(restored), $"Restoring with preference '{preference}' did not settle.");
            return restored;
        }
        var portal = await RestoreAsync("portal", w => w.LoginStatus.Text.Contains("Choose where") && w.LoginPage.IsEnabled);
        Check(!portal.Accepted && portal.IsVisible && portal.ShowingWorkspaces, "The portal preference stops on the workspace picker after a restored session.");
        DashboardSmokeTest.Capture(portal, Path.Combine(outputDirectory, "identity-restored-portal.png"));
        portal.Close();
        await session.SelectWorkspaceAsync(new WorkspaceSelection(createdAirline.Id), CancellationToken.None);
        var lastUsed = await RestoreAsync("last_used", w => !w.IsVisible);
        Check(lastUsed.Accepted && session.Workspace.AirlineId == createdAirline.Id, "The last-used preference resumes the previous airline workspace without showing the picker.");
        var personal = await RestoreAsync("personal", w => !w.IsVisible);
        Check(personal.Accepted && session.Workspace.AirlineId is null, "The personal preference always opens the pilot workspace, even after flying for an airline.");
        waitForCancel = true;
        var closing = new AccountWindow(new DesktopAccountSession(config, Path.Combine(root, "close"), browserLogin: async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new BrowserLoginResult("", null, true);
        }));
        closing.Show();
        Click(closing.OtherLoginButton);
        closing.Close();
        await IdleAsync(closing);
        Check(!closing.Accepted && !closing.IsVisible, "Closing pending sign-in must cancel it without opening a workspace.");
        return checks;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
