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
        var passwordLogins = 0;
        var mfaLogin = false;
        var importedFlights = 0;
        static AirlineWorkspace Owned(string slug, string name, string callsign) => new(Guid.NewGuid(), slug, name, callsign, AirlinePlan.Community, "active", MembershipStatus.Active,
            [AirlineRole.Pilot, AirlineRole.Owner], true, Capabilities.ForMembership(MembershipStatus.Active, [AirlineRole.Pilot, AirlineRole.Owner]));
        var createdAirline = Owned("created-airline", "Created Airline", "CRT");
        var joinedAirline = new AirlineWorkspace(Guid.NewGuid(), "friends", "Friends Virtual", "FRV", AirlinePlan.Pro, "active", MembershipStatus.Active, [AirlineRole.Pilot], false, Capabilities.ForMembership(MembershipStatus.Active, [AirlineRole.Pilot]));
        var session = new DesktopAccountSession(config, root, () => new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/oauth/token")
            {
                var form = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                if (form.Contains("password-realm"))
                {
                    passwordLogins++;
                    if (!form.Contains("password=correct-horse")) return new(HttpStatusCode.Forbidden) { Content = JsonContent.Create(new { error = "invalid_grant", error_description = "Wrong email or password." }) };
                    if (mfaLogin) return new(HttpStatusCode.Forbidden) { Content = JsonContent.Create(new { error = "mfa_required", mfa_token = "mfa-token-1" }) };
                }
                else if (form.Contains("mfa-otp") && !form.Contains("otp=123456"))
                    return new(HttpStatusCode.Forbidden) { Content = JsonContent.Create(new { error = "invalid_grant", error_description = "Invalid otp_code." }) };
                return revoked ? new(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = "invalid_grant" }) }
                    : new(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = "test-access", refresh_token = "test-refresh", token_type = "Bearer", expires_in = 300 }) };
            }
            if (path == "/mfa/authenticators") return new(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<object>()) };
            if (path == "/mfa/associate") return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { authenticator_type = "otp", secret = "JBSWY3DPEHPK3PXP", barcode_uri = "otpauth://totp/x", recovery_codes = new[] { "RECOVER1234" } }) };
            if (path == "/api/v1/me/flights/summary") return new(HttpStatusCode.OK) { Content = JsonContent.Create(new LogbookSummary(importedFlights, importedFlights * 55, 2, importedFlights > 0 ? new[] { "A20N" } : Array.Empty<string>(), now, now)) };
            if (path == "/api/v1/me/flights" && request.Method == HttpMethod.Get) return new(HttpStatusCode.OK) { Content = JsonContent.Create(new LogbookPage(Enumerable.Range(0, importedFlights).Select(i => new FlightLogEntry(Guid.NewGuid(), "import", "CZK10" + i, "KMKE", "KORD", "A20N", "", now.AddDays(-i), null, 55, null, null, -180, null, "", "", null, now)).ToArray(), importedFlights, 1, 6)) };
            if (path == "/api/v1/me/flights/import") { importedFlights = 2; return new(HttpStatusCode.OK) { Content = JsonContent.Create(new LogbookImportResult(Guid.NewGuid(), 2, 0, 1, new[] { "Line 4: bad date" }, new[] { "date", "flight", "origin", "destination" })) }; }
            if (path.StartsWith("/api/v1/me/flights/imports/")) { importedFlights = 0; return new(HttpStatusCode.NoContent); }
            if (path == "/api/v1/me/avatar" && request.Method == HttpMethod.Put)
            {
                var profile = (bootstrap.Profile ?? UserProfile.Default) with { AvatarUrl = "/media/avatar/" + bootstrap.Account.Id.ToString("N") + "?v=1" };
                bootstrap = bootstrap with { Profile = profile };
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(profile) };
            }
            if (path.StartsWith("/media/")) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(SmokePng) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png") } } };
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
            Check(loginCalls == 0 && passwordLogins == 0 && window.LoginStatus.Text.Contains("password"), "In-app sign-in asks for the password before contacting the provider.");
            window.UpdateLayout();
            DashboardSmokeTest.Capture(window, Path.Combine(outputDirectory, "identity-login.png"));
            Click(window.OtherLoginButton);
            Check(!window.LoginPage.IsEnabled && window.CancelSignInButton.IsVisible && window.CancelSignInButton.IsEnabled,
                "Browser sign-in must disable duplicate submissions while keeping Cancel usable.");
            window.UpdateLayout();
            DashboardSmokeTest.Capture(window, Path.Combine(outputDirectory, "identity-login-pending.png"));
            Click(window.OtherLoginButton);
            Check(loginCalls == 1, "Repeated sign-in clicks must not start concurrent login flows.");
            Click(window.CancelSignInButton);
            await IdleAsync(window);
            Check(!window.ShowingWorkspaces && session.Bootstrap is null && window.LoginStatus.Text.Contains("canceled") && !window.CancelSignInButton.IsVisible,
                "Canceled sign-in must restore a usable login screen without a session.");
            waitForCancel = false;
            window.EmailInput.Text = "pilot@example.invalid"; window.PasswordInput.Password = "wrong";
            Click(window.ContinueButton);
            await IdleAsync(window);
            Check(passwordLogins == 1 && !window.ShowingWorkspaces && window.LoginStatus.Text.Contains("Wrong email or password") && window.PasswordInput.Password.Length == 0,
                "A wrong password is explained and the field is cleared.");
            window.PasswordInput.Password = "correct-horse";
            Click(window.PeekButton);
            Check(window.PasswordRevealInput.IsVisible && window.PasswordRevealInput.Text == "correct-horse" && !window.PasswordInput.IsVisible, "Peek reveals the typed password in place.");
            Click(window.PeekButton);
            Check(window.PasswordInput.IsVisible && window.PasswordInput.Password == "correct-horse", "Hiding the password keeps what was typed.");
            window.RememberPasswordCheck.IsChecked = true;
            Click(window.ContinueButton);
            await IdleAsync(window);
            Check(passwordLogins == 2 && loginCalls == 1 && window.ShowingWorkspaces && session.Bootstrap is not null && !window.Accepted,
                "Password sign-in establishes the session in the app without opening the browser.");
            Check(session.RememberedLogin is { Email: "pilot@example.invalid", Password: "correct-horse" } && !File.ReadAllText(Path.Combine(root, "Identity", "remembered.bin"), System.Text.Encoding.Latin1).Contains("correct-horse"),
                "Remembered credentials are stored protected, not in plain text.");
            DashboardSmokeTest.Capture(window, Path.Combine(outputDirectory, "identity-signed-in-app.png"));
            mfaLogin = true;
            var mfaOutcome = await session.PasswordLoginAsync("pilot@example.invalid", "correct-horse", true, CancellationToken.None);
            var mfaDialog = new MfaWindow(window, session, mfaOutcome.MfaToken!, mfaOutcome.NeedsEnrollment, true);
            mfaDialog.Show(); await Task.Delay(50); mfaDialog.UpdateLayout();
            Check(mfaDialog.Inputs<TextBox>().Any(t => t.Text.Replace(" ", "") == "JBSWY3DPEHPK3PXP"), "Enrolment shows the authenticator secret for manual entry.");
            DashboardSmokeTest.Capture(mfaDialog, Path.Combine(outputDirectory, "identity-mfa-enrol.png"));
            mfaDialog.Input<TextBox>("Code from your authenticator app").Text = "123456";
            await mfaDialog.SubmitAsync(); mfaDialog.UpdateLayout();
            Check(mfaDialog.Completed && mfaDialog.RecoveryCodesShown.SequenceEqual(new[] { "RECOVER1234" }) && mfaDialog.IsVisible, "A verified code completes MFA and shows the recovery code once.");
            DashboardSmokeTest.Capture(mfaDialog, Path.Combine(outputDirectory, "identity-mfa-recovery.png"));
            mfaDialog.Close(); mfaLogin = false;
            window.EmailInput.Text = "";
            Click(window.RegisterBrowserButton);
            await IdleAsync(window);
            Check(signup && window.ShowingWorkspaces && session.Bootstrap is not null && !window.Accepted,
                "Browser registration must establish a desktop session and wait for explicit workspace selection.");
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
            var logbook = new LogbookWindow(window, session) { PickFileOverride = () => "date,flight,origin,destination\n2026-03-14 18:20,CZK101,KMKE,KORD\n" };
            logbook.Show(); await Task.Delay(50);
            await logbook.SubmitAsync(); logbook.UpdateLayout();
            Check(logbook.Imported is { Created: 2 } && logbook.StatusText.Contains("2 added") && logbook.Inputs<TextBlock>().Any(t => t.Text.Contains("2 flights")), $"The logbook dialog imports a CSV and refreshes totals ({logbook.StatusText}).");
            DashboardSmokeTest.Capture(logbook, Path.Combine(outputDirectory, "identity-logbook.png"));
            logbook.Close();
            revoked = true;
            Click(window.ReconnectButton);
            await IdleAsync(window);
            Check(!window.ShowingWorkspaces && session.Bootstrap is null && window.LoginStatus.Text.Contains("expired"),
                "Revoked credentials must return the selector to sign-in.");
            Check(window.EmailInput.Text == "pilot@example.invalid" && window.PasswordInput.Password == "correct-horse" && window.RememberPasswordCheck.IsChecked == true,
                "The sign-in form is prefilled from the remembered login.");
            window.RememberPasswordCheck.IsChecked = false; window.RememberEmailCheck.IsChecked = false;
            revoked = false;
            Click(window.ContinueButton);
            await IdleAsync(window);
            Check(window.ShowingWorkspaces && session.RememberedLogin == RememberedLogin.None, "Unticking both options forgets the stored login on the next sign-in.");
            revoked = true;
            Click(window.ReconnectButton);
            await IdleAsync(window);
            Check(!window.ShowingWorkspaces && window.PasswordInput.Password.Length == 0 && window.RememberPasswordCheck.IsChecked == false, "No password is prefilled once the login is forgotten.");
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

    private static readonly byte[] SmokePng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
