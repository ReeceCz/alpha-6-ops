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
        var session = new DesktopAccountSession(config, root, () => new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/oauth/token")
                return revoked ? new(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = "invalid_grant" }) }
                    : new(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = "test-access", refresh_token = "test-refresh", token_type = "Bearer", expires_in = 300 }) };
            if (request.RequestUri.AbsolutePath == "/api/v1/me/bootstrap")
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(bootstrap) };
            return new(HttpStatusCode.NoContent);
        }), async (request, token) =>
        {
            loginCalls++;
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
            Check(window.EmptyAirlines.IsVisible && window.ReconnectButton.IsVisible && window.ReconnectButton.Content.ToString() == "Refresh",
                $"New accounts must show personal workspace and membership refresh (empty: {window.EmptyAirlines.IsVisible}, refresh: {window.ReconnectButton.IsVisible}, label: {window.ReconnectButton.Content}).");
            bootstrap = bootstrap with { Airlines = [new(Guid.NewGuid(), "new-air", "New Airline", "NEW", AirlinePlan.Community, "active", MembershipStatus.Active, [AirlineRole.Pilot], false, [])] };
            Click(window.ReconnectButton);
            await IdleAsync(window);
            Check(window.AirlineCards.Children.Count == 1 && loginCalls == 2, "Refresh must load new memberships without another browser sign-in.");
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
