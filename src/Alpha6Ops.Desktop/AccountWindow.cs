using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Mail;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Alpha6Ops.Identity;

namespace Alpha6Ops.Desktop;

internal sealed partial class AccountWindow : Window
{
    private readonly DesktopAccountSession? account;
    private readonly bool preview;
    private readonly Action? openPilotPreview;
    private readonly CancellationTokenSource lifetime = new();
    private WorkspaceSelection selection = WorkspaceSelection.Personal;
    private string selectedName = "Flying as a Pilot";
    private bool busy;
    private CancellationTokenSource? signInCancellation;
    internal bool Accepted { get; private set; }
    internal bool SignedOut { get; private set; }
    internal bool ShowingWorkspaces => WorkspacePage.Visibility == Visibility.Visible;
    internal WorkspaceSelection SelectedWorkspace => selection;

    internal AccountWindow(DesktopAccountSession? account, bool restore = false, bool preview = false, Action? openPilotPreview = null)
    {
        this.account = account;
        this.preview = preview;
        this.openPilotPreview = preview ? openPilotPreview : null;
        InitializeComponent();
        OpsUi.Configure(this, preview ? "Account portal - Design preview" : "Account portal", 1240, 820);
        MinWidth = 960; MinHeight = 680;
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        PreviewBanner.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
        PreviewWorkspaceBanner.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
        FooterVersionText.Text = $"ALPHA 6 OPS     v{typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";
        Closed += (_, _) => lifetime.Cancel();
        SizeChanged += (_, _) => UpdateCompactLayout();
        if (account?.Bootstrap is not null) ShowWorkspaces(); else ShowLogin();
        Loaded += (_, _) => { if (!ShowingWorkspaces) EmailInput.Focus(); };
        if (restore && account is not null) Loaded += async (_, _) => await RunAsync(async () =>
        {
            SetStatus("Checking your saved session…");
            if (!await account.RestoreAsync(lifetime.Token)) { ShowLogin(); return; }
            await OpenPreferredWorkspaceAsync();
        });
    }

    // Honors the profile's "Open at sign-in" choice once a saved session is restored: "portal" stops on
    // the workspace picker, "personal" always opens the pilot workspace, anything else resumes the last one.
    private async Task OpenPreferredWorkspaceAsync()
    {
        switch (account!.Profile.PreferredWorkspace)
        {
            case "portal":
                ShowWorkspaces();
                SetStatus("Welcome back. Choose where you’re flying today.");
                return;
            case "personal" when account.Workspace.AirlineId is not null:
                await account.SelectWorkspaceAsync(WorkspaceSelection.Personal, lifetime.Token);
                break;
        }
        Accepted = true; Close();
    }

    private void UpdateCompactLayout()
    {
        var compact = ActualHeight < 760;
        LoginFrame.Margin = compact ? new Thickness(40, 24, 40, 20) : new Thickness(56, 34, 56, 26);
        LoginBody.MinHeight = compact ? 0 : 540;
        LoginForm.Margin = new Thickness(0, compact ? 8 : 16, 0, compact ? 8 : 16);
        LoginHeading.FontSize = compact ? 30 : 36;
        LoginHeading.LineHeight = compact ? 36 : 44;
        LoginDescription.Margin = new Thickness(0, 0, 0, compact ? 12 : 18);
        LoginDivider.Margin = new Thickness(0, compact ? 10 : 14, 0, compact ? 10 : 14);
        LoginOptionsHint.Margin = new Thickness(0, compact ? 6 : 8, 0, compact ? 10 : 14);
    }

    private void ShowLogin()
    {
        LoginPage.Visibility = Visibility.Visible;
        WorkspacePage.Visibility = Visibility.Collapsed;
        ContinueButton.IsDefault = true; OpenWorkspaceButton.IsDefault = false;
        SetStatus(account is null && !preview ? "This installation is in Local Preview. Account sign-in has not been connected yet." : "");
        ContinueButton.IsEnabled = OtherLoginButton.IsEnabled = RegisterButton.IsEnabled = RegisterBrowserButton.IsEnabled = ForgotButton.IsEnabled = PasswordInput.IsEnabled = preview || account is not null;
        ContinueButton.Content = preview ? "Preview your workspaces    →" : "Sign in    →";
    }

    // In-app sign-in: email and password go to the provider's password grant; a second factor is handled in the
    // MfaWindow. Passkeys and Microsoft/Google accounts use the browser path below.
    private async void Continue_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (preview) { ShowWorkspaces(); return; }
        var email = EmailInput.Text.Trim();
        if (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
        { SetStatus("Enter a valid email address to continue."); EmailInput.Focus(); return; }
        if (PasswordInput.Password.Length == 0) { SetStatus("Enter your password, or use passkey / Microsoft / Google sign-in below."); PasswordInput.Focus(); return; }
        if (account is null) return;
        SetStatus("Signing in…");
        PasswordLoginOutcome outcome;
        try { outcome = await account.PasswordLoginAsync(email, PasswordInput.Password, false, lifetime.Token); }
        finally { PasswordInput.Clear(); }
        await CompleteSignInAsync(outcome);
    });

    private async Task CompleteSignInAsync(PasswordLoginOutcome outcome)
    {
        if (!outcome.Success)
        {
            var mfa = new MfaWindow(this, account!, outcome.MfaToken!, outcome.NeedsEnrollment, false);
            mfa.ShowDialog();
            if (!mfa.Completed) { SetStatus("Sign-in was not completed."); return; }
        }
        if (lifetime.IsCancellationRequested) return;
        await OpenPreferredAfterSignInAsync();
    }

    private async Task OpenPreferredAfterSignInAsync()
    {
        ShowWorkspaces();
        if (account!.Profile.PreferredWorkspace == "personal" && account.Workspace.AirlineId is not null)
            await account.SelectWorkspaceAsync(WorkspaceSelection.Personal, lifetime.Token);
    }

    private async void Forgot_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (preview || account is null) { SetStatus("In the connected app, this emails you a password reset link."); return; }
        var email = EmailInput.Text.Trim();
        if (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email) { SetStatus("Enter your email address first, then choose Forgot password."); EmailInput.Focus(); return; }
        await account.RequestPasswordResetAsync(email, lifetime.Token);
        SetStatus($"If an account exists for {email}, a reset link is on its way. Check your inbox, then sign in with the new password.");
    });

    private async void OtherLogin_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (preview) { ShowWorkspaces(); return; }
        await SignInAsync(null);
    });

    private async void Register_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (preview) { ShowWorkspaces(); return; }
        if (account is null) return;
        var dialog = new SignUpWindow(this, account, EmailInput.Text.Trim()); dialog.ShowDialog();
        if (dialog.SignedIn) { await OpenPreferredAfterSignInAsync(); SetStatus("Welcome aboard. Verify your email from the message we sent to unlock airline features."); }
        else if (dialog.MfaToken is { } token) await CompleteSignInAsync(new(false, token, dialog.NeedsEnrollment));
    });

    private async void RegisterBrowser_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (preview) { ShowWorkspaces(); return; }
        var email = EmailInput.Text.Trim();
        if (email.Length > 0 && (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email))
        { SetStatus("Enter a valid email address or leave it blank to create an account in your browser."); EmailInput.Focus(); return; }
        await SignInAsync(email.Length == 0 ? null : email, createAccount: true);
    });

    private async Task SignInAsync(string? email, bool createAccount = false)
    {
        if (account is null) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        signInCancellation = cancellation;
        CancelSignInButton.Visibility = Visibility.Visible;
        SetStatus("Complete sign-in in your browser. You can return here when you’re done.");
        try
        {
            await account.LoginAsync(cancellation.Token, email, createAccount);
            if (!lifetime.IsCancellationRequested) ShowWorkspaces();
        }
        finally { signInCancellation = null; CancelSignInButton.Visibility = Visibility.Collapsed; }
    }

    private void CancelSignIn_Click(object sender, RoutedEventArgs e) => signInCancellation?.Cancel();

    internal void ShowWorkspaces()
    {
        if (!preview && account?.Bootstrap is null) return;
        LoginPage.Visibility = Visibility.Collapsed;
        WorkspacePage.Visibility = Visibility.Visible;
        ContinueButton.IsDefault = false; OpenWorkspaceButton.IsDefault = true;
        var bootstrap = account?.Bootstrap;
        var offline = account?.IsOffline == true;
        AccountNameText.Text = preview ? "Alex Morgan" : bootstrap!.Account.DisplayName;
        AccountEmailText.Text = (preview ? "Design preview · sample memberships" : bootstrap!.Account.Email).ToUpperInvariant();
        AccountInitialsText.Text = account?.Profile.AvatarInitials is { Length: > 0 } initials ? initials
            : string.Concat(AccountNameText.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));
        AccountAvatarImage.Source = null; AccountAvatarImage.Visibility = Visibility.Collapsed;
        if (account?.Profile.AvatarUrl is { Length: > 0 } avatarUrl) _ = ShowMediaAsync(avatarUrl, bytes => { if (Decode(bytes, 80) is { } image) { AccountAvatarImage.Source = image; AccountAvatarImage.Visibility = Visibility.Visible; } });
        ConnectionText.Text = preview ? "DESIGN PREVIEW" : offline ? "OFFLINE ACCESS" : "SIGNED IN";
        PresenceDot.Fill = OpsUi.Brush(preview ? "#8C9AA6" : offline ? "#D6A34A" : "#5FAE6E");
        PlanText.Text = preview ? "FREE" : bootstrap!.PersonalEntitlement.Plan.ToString().ToUpperInvariant();
        var unverified = !preview && !bootstrap!.Account.EmailVerified;
        SetStatus(offline ? "You’re offline. Continue with your current workspace or fly personally. Reconnect to switch airlines." : "");
        CrewHintText.Text = unverified ? "Verify your email address from the message in your inbox, then select Refresh to create or join airlines." : "A new crew. A new destination.";
        CrewHintText.Foreground = OpsUi.Brush(unverified ? "#E5C977" : "#9EAFBD");
        ReconnectButton.Visibility = preview ? Visibility.Collapsed : Visibility.Visible;
        ReconnectLabel.Text = offline ? "Reconnect" : "Refresh";
        ManageButton.IsEnabled = ProfileButton.IsEnabled = LogbookButton.IsEnabled = PlanButton.IsEnabled = UpdatesButton.IsEnabled = !offline;
        JoinButton.IsEnabled = CreateButton.IsEnabled = !offline && !unverified;
        selection = account?.Workspace ?? WorkspaceSelection.Personal;
        var airlines = preview ? PreviewAirlines : bootstrap!.Airlines;
        AirlineCountText.Text = $"YOUR VIRTUAL AIRLINES  /  {airlines.Length:00}";
        AirlineCards.Children.Clear();
        foreach (var airline in airlines)
        {
            var workspace = new WorkspaceSelection(airline.Id);
            var card = AirlineCard(airline, workspace);
            card.IsEnabled = airline.MembershipStatus == MembershipStatus.Active && (!offline || workspace == account!.Workspace);
            AirlineCards.Children.Add(card);
        }
        EmptyAirlines.Visibility = airlines.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        selectedName = selection.AirlineId is { } id ? airlines.FirstOrDefault(a => a.Id == id)?.Name ?? "Flying as a Pilot" : "Flying as a Pilot";
        UpdateSelection();
        OpenWorkspaceButton.Focus();
    }

    private Button AirlineCard(AirlineWorkspace airline, WorkspaceSelection workspace)
    {
        var card = new Button { Style = (Style)FindResource("WorkspaceCard"), Tag = workspace, Margin = new Thickness(0, 0, 16, 16), MinHeight = 166, Padding = new Thickness(22) };
        System.Windows.Automation.AutomationProperties.SetName(card, airline.Name + ", " + string.Join(", ", airline.Roles));
        var content = new Grid();
        for (var i = 0; i < 3; i++) content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new Grid();
        var monogram = Copy(airline.Callsign.Length > 3 ? airline.Callsign[..3] : airline.Callsign, 12, "#D5E2ED", FontWeights.SemiBold);
        monogram.HorizontalAlignment = HorizontalAlignment.Center; monogram.VerticalAlignment = VerticalAlignment.Center;
        var tile = new Border { Background = OpsUi.Brush("#0F1922"), BorderBrush = OpsUi.Brush("#3A4B5B"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Width = 42, Height = 42, HorizontalAlignment = HorizontalAlignment.Left, Child = monogram };
        header.Children.Add(tile);
        if (!preview && airline.LogoUrl.Length > 0) _ = ShowMediaAsync(airline.LogoUrl, bytes => { if (ImageTile(bytes, 40) is { } logo) tile.Child = logo; });
        var tier = Copy(airline.Plan.ToString().ToUpperInvariant(), 10, "#A2B0BD"); tier.HorizontalAlignment = HorizontalAlignment.Right; tier.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(tier); content.Children.Add(header);
        if (!preview && airline.Capabilities.Contains(Capabilities.MembersManage))
        {
            var manage = new Button { Content = "Manage", Style = (Style)FindResource("PortalLink"), FontSize = 12, Padding = new Thickness(0, 4, 0, 4), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 22) };
            System.Windows.Automation.AutomationProperties.SetName(manage, "Manage " + airline.Name);
            manage.Click += async (_, e) => { e.Handled = true; await RunAsync(() => ManageAirlineAsync(airline)); };
            var cardHeader = new Grid(); cardHeader.Children.Add(manage); Grid.SetRow(cardHeader, 2); cardHeader.VerticalAlignment = VerticalAlignment.Bottom; cardHeader.HorizontalAlignment = HorizontalAlignment.Right; content.Children.Add(cardHeader);
        }
        var name = Copy(airline.Name, 19, "#E8EDF2", FontWeights.SemiBold); name.Margin = new Thickness(0, 17, 0, 8); Grid.SetRow(name, 1); content.Children.Add(name);
        var roles = Copy(string.Join(" · ", airline.Roles) + (airline.IsFounder ? " · Founder" : ""), 12, "#A2B0BD"); Grid.SetRow(roles, 2); content.Children.Add(roles);
        card.Content = content;
        card.Click += (_, _) => { selection = workspace; selectedName = airline.Name; UpdateSelection(); };
        return card;
    }

    // Media is fetched without credentials and rendered only if it decodes as a bitmap; failures leave the fallback.
    private async Task ShowMediaAsync(string url, Action<byte[]> apply)
    {
        try
        {
            var bytes = await account!.FetchMediaAsync(url, lifetime.Token);
            if (bytes is not null && !lifetime.IsCancellationRequested) apply(bytes);
        }
        catch (Exception) { }
    }
    internal static System.Windows.Media.Imaging.BitmapImage? Decode(byte[] bytes, int pixels)
    {
        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit(); image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; image.DecodePixelWidth = pixels * 2;
            image.StreamSource = new System.IO.MemoryStream(bytes); image.EndInit(); image.Freeze();
            return image;
        }
        catch (Exception) { return null; }
    }
    internal static UIElement? ImageTile(byte[] bytes, double size)
    {
        if (Decode(bytes, (int)size) is not { } image) return null;
        var picture = new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(3) };
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = picture };
    }

    private static TextBlock Copy(string text, double size, string color, FontWeight? weight = null) => new()
    { Text = text, FontFamily = new FontFamily("Segoe UI"), FontSize = size, Foreground = OpsUi.Brush(color), FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap };

    private void Personal_Click(object sender, RoutedEventArgs e)
    { selection = WorkspaceSelection.Personal; selectedName = "Flying as a Pilot"; UpdateSelection(); }

    private void UpdateSelection()
    {
        var personalSelected = selection.AirlineId is null;
        Style(PersonalCard, personalSelected);
        PersonalSelected.Inlines.Clear();
        if (personalSelected)
        {
            PersonalSelected.Inlines.Add(new Run("●  ") { Foreground = OpsUi.Brush("#5FAE6E") });
            PersonalSelected.Inlines.Add(new Run("ACTIVE") { Foreground = OpsUi.Brush("#E5C44A") });
        }
        else PersonalSelected.Inlines.Add(new Run("SELECT  ↗") { Foreground = OpsUi.Brush("#D8E1E6") });
        foreach (var card in AirlineCards.Children.OfType<Button>())
            Style(card, (WorkspaceSelection)card.Tag == selection);
        SelectedNameText.Text = selectedName;
        SelectedTypeText.Text = personalSelected ? "PERSONAL WORKSPACE" : "VIRTUAL AIRLINE WORKSPACE";

        // Selection is a lit gold frame with a soft glow; the card surface itself never changes so the
        // photo gradient inside the card stays seamless.
        static void Style(Button card, bool selected)
        {
            card.BorderBrush = OpsUi.Brush(selected ? "#E5C44A" : "#2E3F4E");
            card.BorderThickness = new Thickness(selected ? 2 : 1);
            card.Effect = selected ? new DropShadowEffect { Color = (Color)ColorConverter.ConvertFromString("#E5C44A"), BlurRadius = 26, ShadowDepth = 0, Opacity = 0.32 } : null;
        }
    }

    private async void OpenWorkspace_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (preview)
        {
            if (selection.AirlineId is null && openPilotPreview is not null) { openPilotPreview(); Close(); return; }
            SetStatus(openPilotPreview is not null ? "Choose Flying as a Pilot to explore the current app. Airline workspaces are not available in this preview."
                : $"“{selectedName}” selected. This design preview ends here; signing in to a configured account opens your workspace.");
            return;
        }
        await account!.SelectWorkspaceAsync(selection, lifetime.Token); Accepted = true; Close();
    });

    private void Back_Click(object sender, RoutedEventArgs e) { if (preview) ShowLogin(); else Close(); }

    private bool RequireAccount(string previewMessage)
    {
        if (preview) { SetStatus(previewMessage); return false; }
        if (account?.Bootstrap is null) { SetStatus("Sign in to use account services."); return false; }
        if (account.IsOffline) { SetStatus("Connect to the internet and select Reconnect to use account services."); return false; }
        return true;
    }

    private async void Profile_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        if (!RequireAccount("In the connected app, this opens your pilot profile: SimBrief username, callsign, home base and units.")) return Task.CompletedTask;
        var dialog = new ProfileWindow(this, account!, account!.DataDirectory); dialog.ShowDialog();
        if (dialog.Saved is not null) { ShowWorkspaces(); SetStatus("Profile saved. Dispatch will use your SimBrief username automatically."); }
        else if (dialog.PictureChanged) ShowWorkspaces();
        return Task.CompletedTask;
    });

    private async void Logbook_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        if (!RequireAccount("In the connected app, this shows your account logbook and imports a CSV from another platform.")) return Task.CompletedTask;
        new LogbookWindow(this, account!).ShowDialog();
        return Task.CompletedTask;
    });

    private async void Plan_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        if (!RequireAccount("In the connected app, this lets you choose between the Free and Premium account levels.")) return Task.CompletedTask;
        var dialog = new PlanSelectionWindow(this, account!); dialog.ShowDialog();
        if (dialog.Changed) { ShowWorkspaces(); SetStatus($"Your account level is now {account!.Bootstrap!.PersonalEntitlement.Plan}."); }
        return Task.CompletedTask;
    });

    private async void Create_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        if (!RequireAccount("In the connected app, this creates your own virtual airline right here.")) return Task.CompletedTask;
        var dialog = new CreateAirlineWindow(this, account!); dialog.ShowDialog();
        if (dialog.Created is { } created) { ShowWorkspaces(); selection = new WorkspaceSelection(created.Id); selectedName = created.Name; UpdateSelection(); SetStatus($"{created.Name} is ready. Invite your crew from Manage, or open the workspace now."); }
        return Task.CompletedTask;
    });

    private async void Join_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        if (!RequireAccount("In the connected app, this accepts an invitation code from an airline administrator.")) return Task.CompletedTask;
        var dialog = new JoinAirlineWindow(this, account!); dialog.ShowDialog();
        if (dialog.Joined is { } joined) { ShowWorkspaces(); selection = new WorkspaceSelection(joined.Id); selectedName = joined.Name; UpdateSelection(); SetStatus($"Welcome to {joined.Name}. Open the workspace to start flying with your crew."); }
        return Task.CompletedTask;
    });

    private async void Updates_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        if (!RequireAccount("In the connected app, this shows the installed version and any published release.")) return Task.CompletedTask;
        new ReleaseWindow(this, account!).ShowDialog();
        return Task.CompletedTask;
    });

    private async Task ManageAirlineAsync(AirlineWorkspace airline)
    {
        if (!RequireAccount("In the connected app, this opens the airline's members and invitations.")) return;
        var dialog = new AirlineMembersWindow(this, account!, airline); dialog.ShowDialog();
        if (dialog.Changed && await account!.RestoreAsync(lifetime.Token)) ShowWorkspaces();
    }

    private void Portal_Click(object sender, RoutedEventArgs e)
    {
        if (preview) { SetStatus("In the connected app, this opens the account website for registration and airline management."); return; }
        if (account is null) { SetStatus("Account services have not been configured for this installation."); return; }
        var path = (sender as Button)?.Tag as string ?? "Account";
        try
        {
            Process.Start(new ProcessStartInfo(new Uri(new Uri(account.Configuration.PortalUrl.TrimEnd('/') + "/"), path).AbsoluteUri) { UseShellExecute = true });
            if (ShowingWorkspaces) SetStatus("Complete your changes on the website, then select Refresh to update your workspaces.");
        }
        catch (Exception) { SetStatus("The website could not be opened. Check your default browser and try again."); }
    }

    private async void SignOut_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (preview) { ShowLogin(); return; }
        await account!.SignOutAsync(); SignedOut = true; Close();
    });

    private async void Reconnect_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (preview || account is null) return;
        if (!account.CanRefresh) { await SignInAsync(null); return; }
        SetStatus("Refreshing your account and workspaces…");
        if (await account.RestoreAsync(lifetime.Token))
        {
            ShowWorkspaces();
            if (account.IsOffline) SetStatus("Could not reconnect. Your saved workspace is still available offline. Try again when your connection returns.");
        }
        else { ShowLogin(); SetStatus("Your session has expired. Sign in again to continue."); }
    });

    private void SetStatus(string message)
    {
        LoginStatus.Text = WorkspaceStatus.Text = message;
        LoginStatus.Visibility = WorkspaceStatus.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true; LoginPage.IsEnabled = WorkspacePage.IsEnabled = false;
        try { await action(); }
        catch (OperationCanceledException) { RecoverAfterError("Sign-in canceled. You can try again."); }
        catch (AccountSessionException error) { RecoverAfterError(error.Message); }
        catch (SocketException) { RecoverAfterError("The sign-in callback could not start. Close any other sign-in attempt and try again."); }
        catch (HttpRequestException) { RecoverAfterError("The account service could not be reached. Check your connection and try again."); }
        catch (Exception) { RecoverAfterError("We couldn’t complete that request. Please try again."); }
        finally { busy = false; LoginPage.IsEnabled = WorkspacePage.IsEnabled = true; }
    }

    private void RecoverAfterError(string message)
    {
        if (lifetime.IsCancellationRequested) return;
        if (!preview)
        {
            if (account?.Bootstrap is null) ShowLogin();
            else ShowWorkspaces();
        }
        SetStatus(message);
    }

    // Presentation-only samples; never create a session or enter an authenticated workspace.
    private static readonly AirlineWorkspace[] PreviewAirlines =
    [
        new(new Guid("11111111-1111-1111-1111-111111111111"), "northstar", "Northstar Virtual", "NSV", AirlinePlan.Community, "active", MembershipStatus.Active, [AirlineRole.Owner], true, []),
        new(new Guid("22222222-2222-2222-2222-222222222222"), "meridian", "Meridian Air", "MVA", AirlinePlan.Pro, "active", MembershipStatus.Active, [AirlineRole.Pilot], false, [])
    ];
}
