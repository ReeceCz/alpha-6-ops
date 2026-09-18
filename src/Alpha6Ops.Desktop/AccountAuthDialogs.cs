using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Alpha6Ops.Identity;

namespace Alpha6Ops.Desktop;

// Second factor inside the app: verify a code from an authenticator app, enrol one when the account has none
// (secret shown for manual entry), or fall back to a recovery code. Codes never touch the session file.
internal sealed class MfaWindow : PortalDialog
{
    private readonly string mfaToken;
    private readonly bool stepUp;
    private readonly TextBox code;
    private readonly Button recoveryToggle;
    private readonly StackPanel afterPanel = new() { Visibility = Visibility.Collapsed };
    private OtpEnrollment? enrollment;
    private bool useRecovery;
    internal bool Completed { get; private set; }
    internal string[] RecoveryCodesShown { get; private set; } = [];

    internal MfaWindow(Window owner, DesktopAccountSession account, string mfaToken, bool needsEnrollment, bool stepUp)
        : base(owner, account, stepUp ? "SECURITY CHECK" : "TWO-STEP SIGN-IN",
            needsEnrollment ? "Add an authenticator app" : "Enter your sign-in code",
            needsEnrollment ? "This account has no second factor yet. Add the secret below to Google Authenticator, Microsoft Authenticator, 1Password or any TOTP app, then enter the code it shows."
                : "Open your authenticator app and enter the 6-digit code for Alpha 6 OPS.",
            "Verify", 620, needsEnrollment ? 700 : 520)
    {
        this.mfaToken = mfaToken; this.stepUp = stepUp;
        if (needsEnrollment)
        {
            var secretText = new TextBox { Style = (Style)FindResource("PortalInput"), IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 16, Text = "Preparing…" };
            System.Windows.Automation.AutomationProperties.SetName(secretText, "Authenticator secret");
            var uriText = new TextBox { Style = (Style)FindResource("PortalInput"), IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 11, Text = "" , Margin = new Thickness(0, 8, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(uriText, "Authenticator link");
            var panel = new StackPanel();
            panel.Children.Add(Caption("Secret key — type or paste it into your authenticator app"));
            panel.Children.Add(secretText);
            panel.Children.Add(new TextBlock { Text = "Or paste this link into an app that accepts otpauth links:", FontSize = 11, Foreground = OpsUi.Brush("#8FA3B1"), Margin = new Thickness(0, 10, 0, 0) });
            panel.Children.Add(uriText);
            Body.Children.Add(Boxed(panel));
            Loaded += async (_, _) => await RunAsync(async () =>
            {
                var started = await Account.BeginOtpEnrollmentAsync(mfaToken, Lifetime.Token);
                enrollment = started;
                secretText.Text = string.Join(" ", Enumerable.Range(0, (started.Secret.Length + 3) / 4).Select(i => started.Secret.Substring(i * 4, Math.Min(4, started.Secret.Length - i * 4))));
                uriText.Text = started.BarcodeUri;
                Inputs<TextBox>().LastOrDefault(t => !t.IsReadOnly)?.Focus();
            });
        }
        code = Field(needsEnrollment ? "Code from your authenticator app" : "6-digit code", "", 24, null, needsEnrollment ? 6 : 0);
        code.FontFamily = new FontFamily("Consolas"); code.FontSize = 20;
        recoveryToggle = new Button { Content = "Use a recovery code instead", Style = (Style)FindResource("PortalLink"), HorizontalAlignment = HorizontalAlignment.Left, Visibility = needsEnrollment ? Visibility.Collapsed : Visibility.Visible };
        recoveryToggle.Click += (_, _) =>
        {
            useRecovery = !useRecovery;
            recoveryToggle.Content = useRecovery ? "Use my authenticator app instead" : "Use a recovery code instead";
            SetHeader(useRecovery ? "Enter a recovery code" : "Enter your sign-in code", useRecovery ? "Use one of the recovery codes you saved when you set up your authenticator. Each code works once; a replacement is shown after you sign in."
                : "Open your authenticator app and enter the 6-digit code for Alpha 6 OPS.");
            code.Text = ""; code.Focus();
        };
        Body.Children.Add(recoveryToggle);
        Body.Children.Add(afterPanel);
        if (!needsEnrollment) Loaded += (_, _) => code.Focus();
    }

    protected override async Task OnPrimaryAsync()
    {
        if (Completed) { Finish(); return; }
        var entered = code.Text.Trim();
        if (entered.Length < 6) { SetStatus(useRecovery ? "Enter the full recovery code." : "Enter the 6-digit code from your app."); return; }
        var replacement = await Account.CompleteMfaAsync(mfaToken, entered, useRecovery, stepUp, Lifetime.Token);
        Completed = true;
        var codes = enrollment?.RecoveryCodes ?? [];
        if (replacement is { Length: > 0 }) codes = [replacement];
        if (codes.Length == 0) { Finish(); return; }
        // Recovery codes are shown exactly once; the dialog stays open until the pilot acknowledges them.
        RecoveryCodesShown = codes;
        code.IsEnabled = false; recoveryToggle.Visibility = Visibility.Collapsed;
        SetHeader("Save your recovery code" + (codes.Length > 1 ? "s" : ""), "If you lose your authenticator, a recovery code is the only way back into your account. Store " + (codes.Length > 1 ? "them" : "it") + " somewhere safe now — " + (codes.Length > 1 ? "they are" : "it is") + " not shown again.");
        var list = new TextBox { Style = (Style)FindResource("PortalInput"), IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 16, Text = string.Join(Environment.NewLine, codes), AcceptsReturn = true, MinHeight = 48 };
        System.Windows.Automation.AutomationProperties.SetName(list, "Recovery codes");
        var copy = new Button { Content = "Copy", Style = (Style)FindResource("PortalButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(18, 8, 18, 8) };
        copy.Click += (_, _) => { try { Clipboard.SetText(string.Join(Environment.NewLine, codes)); SetStatus("Copied. Paste it into your password manager."); } catch (Exception) { SetStatus("Copy failed — select the text and copy it manually."); } };
        var panel = new StackPanel(); panel.Children.Add(list); panel.Children.Add(copy);
        afterPanel.Children.Add(Boxed(panel, "#A68D3B")); afterPanel.Visibility = Visibility.Visible;
        Primary.Content = "I saved it — done"; Cancel.Visibility = Visibility.Collapsed;
    }
}

// Creates the account with the provider's database connection and signs straight in. Verification email is
// sent by the provider; airline features stay locked until the address is verified.
internal sealed class SignUpWindow : PortalDialog
{
    private readonly TextBox email;
    private readonly PasswordBox password = new(), confirm = new();
    internal bool SignedIn { get; private set; }
    internal string? MfaToken { get; private set; }
    internal bool NeedsEnrollment { get; private set; }

    internal SignUpWindow(Window owner, DesktopAccountSession account, string initialEmail)
        : base(owner, account, "NEW ACCOUNT", "Create your Alpha 6 account", "Email and password stay with the sign-in provider; Alpha 6 never sees your password. You’ll get a verification email right after.", "Create account", 600, 600)
    {
        email = Field("Email address", initialEmail, 254);
        Body.Children.Add(Caption("Password"));
        StylePassword(password); Body.Children.Add(password);
        Body.Children.Add(new TextBlock { Text = "At least 8 characters, with letters, numbers and a symbol.", FontSize = 11, Foreground = OpsUi.Brush("#8FA3B1"), Margin = new Thickness(0, 6, 0, 14) });
        Body.Children.Add(Caption("Confirm password"));
        StylePassword(confirm); Body.Children.Add(confirm);
        Loaded += (_, _) => (initialEmail.Length > 0 ? (Control)password : email).Focus();
    }

    private static void StylePassword(PasswordBox box)
    {
        box.Background = OpsUi.Brush("#0C1721"); box.Foreground = OpsUi.Brush("#E8EDF2"); box.BorderBrush = OpsUi.Brush("#3A4B5B");
        box.Padding = new Thickness(12, 10, 12, 10); box.FontSize = 14; box.MinHeight = 44; box.CaretBrush = OpsUi.Brush("#E5C44A"); box.MaxLength = 128;
    }

    protected override async Task OnPrimaryAsync()
    {
        var address = email.Text.Trim();
        if (!System.Net.Mail.MailAddress.TryCreate(address, out var parsed) || parsed.Address != address) { SetStatus("Enter a valid email address."); email.Focus(); return; }
        if (password.Password.Length < 8) { SetStatus("Use at least 8 characters."); password.Focus(); return; }
        if (password.Password != confirm.Password) { SetStatus("The passwords don’t match."); confirm.Focus(); return; }
        await Account.SignUpAsync(address, password.Password, Lifetime.Token);
        var outcome = await Account.PasswordLoginAsync(address, password.Password, false, Lifetime.Token);
        password.Clear(); confirm.Clear();
        SignedIn = outcome.Success; MfaToken = outcome.MfaToken; NeedsEnrollment = outcome.NeedsEnrollment;
        Finish();
    }
}

// Fresh proof for an administrative action without leaving the app: password again, then the code. The
// browser remains one click away for passkey and Microsoft/Google accounts.
internal sealed class StepUpWindow : PortalDialog
{
    private readonly PasswordBox password = new();
    internal StepUpChoice Result { get; private set; } = StepUpChoice.Cancelled;

    internal StepUpWindow(Window owner, DesktopAccountSession account)
        : base(owner, account, "SECURITY CHECK", "Confirm your identity", "This action needs a fresh security check. Enter your password, then the code from your authenticator app.", "Continue", 560, 460)
    {
        Body.Children.Add(Caption("Password for " + account.Bootstrap?.Account.Email));
        password.Background = OpsUi.Brush("#0C1721"); password.Foreground = OpsUi.Brush("#E8EDF2"); password.BorderBrush = OpsUi.Brush("#3A4B5B");
        password.Padding = new Thickness(12, 10, 12, 10); password.FontSize = 14; password.MinHeight = 44; password.CaretBrush = OpsUi.Brush("#E5C44A");
        System.Windows.Automation.AutomationProperties.SetName(password, "Password");
        Body.Children.Add(password);
        var browser = new Button { Content = "Use my browser instead (passkey, Microsoft or Google)", Style = (Style)FindResource("PortalLink"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 18, 0, 0) };
        browser.Click += (_, _) => { Result = StepUpChoice.Browser; Finish(); };
        Body.Children.Add(browser);
        Loaded += (_, _) => password.Focus();
    }

    protected override async Task OnPrimaryAsync()
    {
        if (password.Password.Length == 0) { SetStatus("Enter your password."); return; }
        var outcome = await Account.PasswordLoginAsync(Account.Bootstrap!.Account.Email, password.Password, true, Lifetime.Token);
        password.Clear();
        if (outcome.Success) { Result = StepUpChoice.Completed; Finish(); return; }
        var mfa = new MfaWindow(this, Account, outcome.MfaToken!, outcome.NeedsEnrollment, true);
        mfa.ShowDialog();
        if (!mfa.Completed) { SetStatus("The security check was not completed."); return; }
        Result = StepUpChoice.Completed; Finish();
    }
}

// The account logbook: totals from the server, CSV import of another platform's export, undo of the last import.
internal sealed class LogbookWindow : PortalDialog
{
    private readonly TextBlock totals = new() { FontSize = 13, LineHeight = 20, Foreground = OpsUi.Brush("#D8E1E8") };
    private readonly TextBlock recent = new() { FontSize = 12, LineHeight = 19, Foreground = OpsUi.Brush("#A2B0BD"), FontFamily = new FontFamily("Consolas") };
    private readonly Button undo;
    private LogbookImportResult? lastImport;
    internal LogbookImportResult? Imported => lastImport;

    internal LogbookWindow(Window owner, DesktopAccountSession account)
        : base(owner, account, "PILOT LOGBOOK", "Your logbook", "Flights the app files land in your account. Import the logbook you kept elsewhere so it counts from day one.", "Import a CSV file…", 640, 620)
    {
        var box = new StackPanel(); box.Children.Add(totals); box.Children.Add(new Border { Height = 10 }); box.Children.Add(recent);
        Body.Children.Add(Boxed(box));
        Body.Children.Add(new TextBlock { Text = "Import matches columns by name (date, flight, origin, destination, aircraft, block, landing_rate …) so vAMSYS, smartCARS/phpVMS, FsHub and spreadsheet exports all work. Flights already in your logbook are skipped.", FontSize = 12, Foreground = OpsUi.Brush("#A2B0BD"), TextWrapping = TextWrapping.Wrap, LineHeight = 19 });
        undo = new Button { Content = "Undo the last import", Style = (Style)FindResource("PortalLink"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0), Visibility = Visibility.Collapsed };
        undo.Click += async (_, _) => await RunAsync(async () =>
        {
            if (lastImport is null) return;
            await Account.UndoLogbookImportAsync(lastImport.BatchId, Lifetime.Token);
            lastImport = null; undo.Visibility = Visibility.Collapsed;
            await LoadAsync(); SetStatus("Import undone.");
        });
        Body.Children.Add(undo);
        var web = new Button { Content = "Open the full logbook on the web ↗", Style = (Style)FindResource("PortalLink"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        web.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Account.Configuration.PortalUrl.TrimEnd('/') + "/Account/Logbook") { UseShellExecute = true }); } catch (Exception) { SetStatus("Could not open your browser."); } };
        Body.Children.Add(web);
        Cancel.Content = "Close";
        Loaded += async (_, _) => await RunAsync(LoadAsync);
    }

    internal async Task LoadAsync()
    {
        var summary = await Account.LogbookSummaryAsync(Lifetime.Token);
        totals.Text = summary.Flights == 0 ? "No flights in your account logbook yet."
            : $"{summary.Flights:n0} flights · {summary.BlockMinutes / 60:n0}h {summary.BlockMinutes % 60:00}m block · {summary.Airports:n0} airports" + (summary.TopAircraft.Length > 0 ? $" · mostly {string.Join(", ", summary.TopAircraft.Take(3))}" : "");
        var page = await Account.ListFlightsAsync(1, 6, Lifetime.Token);
        recent.Text = string.Join(Environment.NewLine, page.Entries.Select(f => $"{f.DepartureUtc:yyyy-MM-dd HH:mm}  {(f.FlightNumber.Length > 0 ? f.FlightNumber : "—"),-8} {f.Origin}→{f.Destination}  {(f.AircraftType.Length > 0 ? f.AircraftType : "    ")}  {f.BlockMinutes / 60}h{f.BlockMinutes % 60:00}"));
    }

    // Diagnostic hook: the smoke fixture feeds CSV text without a file dialog.
    internal Func<string?>? PickFileOverride { get; set; }

    protected override async Task OnPrimaryAsync()
    {
        string? text;
        string? fileName = null;
        if (PickFileOverride is not null) text = PickFileOverride();
        else
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Import a logbook", Filter = "CSV files (*.csv;*.txt)|*.csv;*.txt|All files|*.*", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            if (new FileInfo(dialog.FileName).Length > 3_000_000) { SetStatus("Files must be 3 MB or smaller."); return; }
            text = await File.ReadAllTextAsync(dialog.FileName, Lifetime.Token); fileName = Path.GetFileName(dialog.FileName);
        }
        if (string.IsNullOrWhiteSpace(text)) return;
        lastImport = await Account.ImportLogbookAsync(text, fileName, Lifetime.Token);
        undo.Visibility = lastImport.Created > 0 ? Visibility.Visible : Visibility.Collapsed;
        await LoadAsync();
        SetStatus($"{lastImport.Created} added, {lastImport.Duplicates} already there, {lastImport.Skipped} skipped." + (lastImport.Errors.Length > 0 ? " First problem: " + lastImport.Errors[0] : ""));
    }
}

internal static class ImagePicker
{
    // Returns null when the pilot cancels. Size is checked here; the server checks the actual image content.
    internal static (byte[] Bytes, string ContentType)? Pick(Window owner, string title)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = "Images (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp", CheckFileExists = true };
        if (dialog.ShowDialog(owner) != true) return null;
        var info = new FileInfo(dialog.FileName);
        if (info.Length > 1024 * 1024) throw new AccountSessionException("Images must be 1 MB or smaller.", "invalid_image");
        var type = info.Extension.ToLowerInvariant() switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
        return (File.ReadAllBytes(dialog.FileName), type);
    }
}
