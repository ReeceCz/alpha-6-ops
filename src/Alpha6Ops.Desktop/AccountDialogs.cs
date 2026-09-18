using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Alpha6Ops.Identity;

namespace Alpha6Ops.Desktop;

// Shared frame for the account portal's modal dialogs: portal palette, eyebrow/title header, scrolling body,
// status line and a Cancel/primary footer. Errors are mapped exactly like AccountWindow.RunAsync.
internal abstract class PortalDialog : Window
{
    protected readonly DesktopAccountSession Account;
    protected readonly StackPanel Body = new();
    protected readonly Button Primary;
    protected readonly Button Cancel;
    private readonly TextBlock status = new() { FontSize = 12, LineHeight = 18, Foreground = OpsUi.Brush("#E5C977"), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock titleText;
    private readonly TextBlock subtitleText;
    private bool busy;
    protected readonly CancellationTokenSource Lifetime = new();

    protected PortalDialog(Window owner, DesktopAccountSession account, string eyebrow, string title, string subtitle, string primaryLabel, double width = 600, double height = 640)
    {
        Owner = owner; Account = account;
        OpsUi.Configure(this, title, width, height);
        MinWidth = Math.Min(width, 520); MinHeight = 420; Background = OpsUi.Brush("#0C141D"); FontFamily = new FontFamily("Segoe UI");
        Resources[typeof(TextBlock)] = new Style(typeof(TextBlock), (Style)FindResource("PortalText"));
        Resources[typeof(ScrollBar)] = new Style(typeof(ScrollBar), (Style)FindResource("PortalScrollBar"));
        Closed += (_, _) => Lifetime.Cancel();
        var root = new Grid { Margin = new Thickness(34, 28, 34, 26) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
        var eyebrowRow = new StackPanel { Orientation = Orientation.Horizontal };
        eyebrowRow.Children.Add(new System.Windows.Shapes.Rectangle { Width = 22, Height = 2, Fill = OpsUi.Brush("#E5C44A"), Margin = new Thickness(0, 0, 10, 0) });
        eyebrowRow.Children.Add(Eyebrow(eyebrow, "#C9D4DC"));
        header.Children.Add(eyebrowRow);
        titleText = new TextBlock { Text = title, FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) };
        subtitleText = new TextBlock { Text = subtitle, FontSize = 13, Foreground = OpsUi.Brush("#A2B0BD"), LineHeight = 20 };
        header.Children.Add(titleText); header.Children.Add(subtitleText);
        root.Children.Add(header);
        var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = Body };
        Grid.SetRow(scroller, 1); root.Children.Add(scroller);
        var footer = new Border { BorderBrush = OpsUi.Brush("#2A3A49"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 18, 0, 0), Margin = new Thickness(0, 18, 0, 0) };
        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new()); footerGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        footerGrid.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        Cancel = new Button { Content = "Cancel", Style = (Style)FindResource("PortalButton"), Padding = new Thickness(20, 12, 20, 12), Margin = new Thickness(0, 0, 12, 0), IsCancel = true };
        Cancel.Click += (_, _) => Dismiss(false);
        Primary = new Button { Content = primaryLabel, Style = (Style)FindResource("PortalPrimary"), Padding = new Thickness(26, 12, 26, 12), IsDefault = true };
        Primary.Click += async (_, _) => await RunAsync(OnPrimaryAsync);
        buttons.Children.Add(Cancel); buttons.Children.Add(Primary);
        Grid.SetColumn(buttons, 1); footerGrid.Children.Add(buttons); footer.Child = footerGrid;
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        Content = root;
    }

    protected abstract Task OnPrimaryAsync();

    protected void SetHeader(string title, string subtitle) { titleText.Text = title; subtitleText.Text = subtitle; Title = "Alpha 6 OPS — " + title; }

    protected static TextBlock Eyebrow(string text, string color = "#96A6B4") => new()
    { Text = text, FontFamily = new FontFamily("Consolas"), FontSize = 10, Foreground = OpsUi.Brush(color), TextWrapping = TextWrapping.Wrap };

    protected TextBlock Caption(string text, double topMargin = 0) => new()
    { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = OpsUi.Brush("#D8E1E8"), Margin = new Thickness(0, topMargin, 0, 8) };

    protected TextBox Field(string label, string initial = "", int maxLength = 0, string? hint = null, double topMargin = 0)
    {
        Body.Children.Add(Caption(label, topMargin));
        var box = new TextBox { Style = (Style)FindResource("PortalInput"), Text = initial, MaxLength = maxLength };
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        Body.Children.Add(box);
        if (hint is not null) Body.Children.Add(new TextBlock { Text = hint, FontSize = 11, Foreground = OpsUi.Brush("#8FA3B1"), Margin = new Thickness(0, 6, 0, 0) });
        Body.Children.Add(new Border { Height = 14 });
        return box;
    }

    protected ComboBox Choice(string label, IEnumerable<(string Value, string Text)> items, string selected, double topMargin = 0)
    {
        Body.Children.Add(Caption(label, topMargin));
        var combo = new ComboBox { Style = (Style)FindResource("PortalCombo") };
        System.Windows.Automation.AutomationProperties.SetName(combo, label);
        foreach (var (value, text) in items) combo.Items.Add(new ComboBoxItem { Content = text, Tag = value });
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == selected) ?? combo.Items.OfType<ComboBoxItem>().FirstOrDefault();
        Body.Children.Add(combo);
        Body.Children.Add(new Border { Height = 14 });
        return combo;
    }

    protected static string Selected(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    protected CheckBox Check(string text, bool isChecked, string? description = null)
    {
        var box = new CheckBox { IsChecked = isChecked, Style = (Style)FindResource("PortalCheck") };
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold });
        if (description is not null) content.Children.Add(new TextBlock { Text = description, FontSize = 11, Foreground = OpsUi.Brush("#8FA3B1"), Margin = new Thickness(0, 2, 0, 0) });
        box.Content = content;
        Body.Children.Add(box);
        return box;
    }

    protected Border Boxed(UIElement child, string border = "#2E3F4E") => new()
    { Child = child, Background = OpsUi.Brush("#121F2A"), BorderBrush = OpsUi.Brush(border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 12) };

    protected void SetStatus(string message)
    {
        status.Text = message; status.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    // Identity confirmation happens in the app (password + authenticator code) unless the pilot chooses the browser.
    protected async Task<StepUpChoice> ConfirmStepUpAsync()
    {
        if (ConfirmOverride is not null) return await ConfirmOverride() ? StepUpChoice.Browser : StepUpChoice.Cancelled;
        var dialog = new StepUpWindow(this, Account);
        dialog.ShowDialog();
        return dialog.Result;
    }

    // Diagnostic hooks: the smoke fixtures drive dialogs without modal loops or confirmation windows.
    internal Func<Task<bool>>? ConfirmOverride { get; set; }
    internal Task SubmitAsync() => RunAsync(OnPrimaryAsync);
    internal string StatusText => status.Text;
    internal T Input<T>(string automationName) where T : FrameworkElement => Descendants(Body).OfType<T>()
        .First(e => System.Windows.Automation.AutomationProperties.GetName(e) == automationName);
    internal IEnumerable<T> Inputs<T>() where T : FrameworkElement => Descendants(Body).OfType<T>();
    private static IEnumerable<FrameworkElement> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element) yield return element;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    protected async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true; Primary.IsEnabled = false; Body.IsEnabled = false;
        try { SetStatus(""); await action(); }
        catch (OperationCanceledException) { SetStatus("Canceled. Nothing was changed."); }
        catch (AccountSessionException error) { SetStatus(error.Message); }
        catch (SocketException) { SetStatus("The sign-in callback could not start. Close any other sign-in attempt and try again."); }
        catch (HttpRequestException) { SetStatus("The account service could not be reached. Check your connection and try again."); }
        catch (Exception) { SetStatus("We couldn’t complete that request. Please try again."); }
        finally { busy = false; Primary.IsEnabled = true; Body.IsEnabled = true; }
    }

    protected void Finish() => Dismiss(true);
    private void Dismiss(bool result)
    {
        if (!IsVisible) return;
        try { DialogResult = result; } catch (InvalidOperationException) { Close(); }
    }
}

// Pilot profile: SimBrief, callsign, home base, units, workspace preference and time zone. Saved server-side
// and mirrored into the local general settings so the dashboard uses the same units immediately.
internal sealed class ProfileWindow : PortalDialog
{
    private readonly TextBox simBrief, callsign, homeBase, initials;
    private readonly ComboBox weight, altitude, landing, workspace, timeZone;
    private readonly string? settingsDirectory;
    internal UserProfile? Saved { get; private set; }
    internal bool PictureChanged { get; private set; }

    internal ProfileWindow(Window owner, DesktopAccountSession account, string? settingsDirectory)
        : base(owner, account, "PILOT PROFILE", "Your pilot profile", "Kept with your account, so every installation picks up the same SimBrief details, units and preferences.", "Save profile", 640, 720)
    {
        this.settingsDirectory = settingsDirectory;
        var profile = account.Profile;
        var pictureRow = new Grid(); pictureRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); pictureRow.ColumnDefinitions.Add(new());
        var preview = new Border { Width = 64, Height = 64, Background = OpsUi.Brush("#0F1922"), BorderBrush = OpsUi.Brush("#3A4B5B"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 16, 0) };
        var previewText = new TextBlock { Text = profile.AvatarInitials.Length > 0 ? profile.AvatarInitials : "A6", FontFamily = new FontFamily("Consolas"), FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = OpsUi.Brush("#E5C44A"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        preview.Child = previewText;
        var pictureButtons = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        pictureButtons.Children.Add(Caption("Profile picture"));
        var pictureActions = new StackPanel { Orientation = Orientation.Horizontal };
        var choose = new Button { Content = "Choose picture…", Style = (Style)FindResource("PortalButton"), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 10, 0) };
        choose.Click += async (_, _) => await RunAsync(async () =>
        {
            var picked = ImagePicker.Pick(this, "Choose a profile picture");
            if (picked is null) return;
            await Account.SetAvatarAsync(picked.Value.Bytes, picked.Value.ContentType, Lifetime.Token);
            PictureChanged = true; preview.Child = AccountWindow.ImageTile(picked.Value.Bytes, 64) ?? previewText; SetStatus("Picture updated.");
        });
        var remove = new Button { Content = "Remove", Style = (Style)FindResource("PortalLink"), Visibility = profile.AvatarUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed };
        remove.Click += async (_, _) => await RunAsync(async () => { await Account.RemoveAvatarAsync(Lifetime.Token); PictureChanged = true; preview.Child = previewText; remove.Visibility = Visibility.Collapsed; SetStatus("Picture removed."); });
        pictureActions.Children.Add(choose); pictureActions.Children.Add(remove);
        pictureButtons.Children.Add(pictureActions);
        pictureButtons.Children.Add(new TextBlock { Text = "PNG, JPEG or WebP up to 1 MB. Shown on your account, to your crew and on the website.", FontSize = 11, Foreground = OpsUi.Brush("#8FA3B1"), Margin = new Thickness(0, 6, 0, 0) });
        Grid.SetColumn(pictureButtons, 1); pictureRow.Children.Add(preview); pictureRow.Children.Add(pictureButtons);
        Body.Children.Add(pictureRow); Body.Children.Add(new Border { Height = 18 });
        if (profile.AvatarUrl.Length > 0) Loaded += async (_, _) => { var bytes = await account.FetchMediaAsync(profile.AvatarUrl, Lifetime.Token); if (bytes is not null && AccountWindow.ImageTile(bytes, 64) is { } tile) preview.Child = tile; };
        simBrief = Field("SimBrief username", profile.SimBriefUsername, 80, "Used to fetch your OFP in Dispatch. Leave blank if you don’t use SimBrief.");
        var row = new Grid(); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new GridLength(16) }); row.ColumnDefinitions.Add(new());
        callsign = new TextBox { Style = (Style)FindResource("PortalInput"), Text = profile.Callsign, MaxLength = 20, CharacterCasing = CharacterCasing.Upper };
        homeBase = new TextBox { Style = (Style)FindResource("PortalInput"), Text = profile.HomeBaseIcao, MaxLength = 4, CharacterCasing = CharacterCasing.Upper };
        var left = new StackPanel(); left.Children.Add(Caption("Pilot callsign")); left.Children.Add(callsign);
        var right = new StackPanel(); right.Children.Add(Caption("Home base (ICAO)")); right.Children.Add(homeBase);
        Grid.SetColumn(right, 2); row.Children.Add(left); row.Children.Add(right);
        Body.Children.Add(row); Body.Children.Add(new Border { Height = 14 });
        weight = Choice("Weight unit", [("LBS", "Pounds (LBS)"), ("KG", "Kilograms (KG)")], profile.WeightUnit);
        altitude = Choice("Altitude unit", [("FT", "Feet (FT)"), ("M", "Metres (M)")], profile.AltitudeUnit);
        landing = Choice("Landing distance unit", [("FT", "Feet (FT)"), ("M", "Metres (M)")], profile.LandingDistanceUnit);
        workspace = Choice("Open at sign-in", [("last_used", "Last used workspace"), ("personal", "Always my personal flight deck"), ("portal", "Account portal — choose every time")], profile.PreferredWorkspace);
        var zones = new List<(string, string)> { ("", "Use this computer’s time zone") };
        zones.AddRange(TimeZoneInfo.GetSystemTimeZones().Select(z => (z.Id, z.DisplayName)));
        timeZone = Choice("Time zone", zones, profile.TimeZone);
        initials = Field("Avatar initials", profile.AvatarInitials, 3, "Up to 3 letters shown on your account badge. Blank uses your name.");
        Loaded += (_, _) => simBrief.Focus();
    }

    protected override async Task OnPrimaryAsync()
    {
        var request = new UpdateProfileRequest(simBrief.Text.Trim(), callsign.Text.Trim(), homeBase.Text.Trim(), Selected(weight), Selected(altitude), Selected(landing), Selected(workspace), Selected(timeZone), initials.Text.Trim());
        if (request.SimBriefUsername.Length == 1 || request.SimBriefUsername.Any(char.IsWhiteSpace)) { SetStatus("SimBrief username must be at least 2 characters with no spaces."); return; }
        if (request.HomeBaseIcao.Length is 1 or 2) { SetStatus("Home base must be a 3 or 4 character airport code, or blank."); return; }
        Saved = await Account.UpdateProfileAsync(request, Lifetime.Token);
        if (settingsDirectory is not null)
        {
            var settings = GeneralSettingsStore.Load(settingsDirectory);
            GeneralSettingsStore.Save(settings with { WeightUnit = Saved.WeightUnit, AltitudeUnit = Saved.AltitudeUnit, LandingDistanceUnit = Saved.LandingDistanceUnit }, settingsDirectory);
        }
        Finish();
    }
}

// Tier cards. No payment is collected in early access: the chosen tier is applied as a complimentary grant.
internal sealed class PlanSelectionWindow : PortalDialog
{
    private readonly AirlineWorkspace? airline;
    private string chosen;
    private readonly List<(Border Card, string Value)> cards = [];
    internal bool Changed { get; private set; }

    internal PlanSelectionWindow(Window owner, DesktopAccountSession account, AirlineWorkspace? airline = null)
        : base(owner, account, airline is null ? "ACCOUNT LEVEL" : "AIRLINE LEVEL",
            airline is null ? "Choose your account level" : $"Choose a level for {airline.Name}",
            "Complimentary during early access — no payment is collected. Levels are independent: a personal level never grants airline administration.",
            "Apply level", 700, 620)
    {
        this.airline = airline;
        chosen = airline is null ? account.Bootstrap?.PersonalEntitlement.Plan.ToString() ?? "Free" : airline.Plan.ToString();
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = new GridLength(16) }); grid.ColumnDefinitions.Add(new());
        if (airline is null)
        {
            grid.Children.Add(Card("Free", "PERSONAL", "Flying as a Pilot", ["Personal logbook and flight tracking", "Dispatch and SimBrief import", "Weather briefings", "Membership in any number of airlines", "Create one Community airline"], 0));
            grid.Children.Add(Card("Premium", "PERSONAL", "Premium pilot", ["Everything in Free", "Cloud logbook sync (in development)", "Extended flight history retention", "Early access to new flight tools", "Priority support"], 2));
        }
        else
        {
            grid.Children.Add(Card("Community", "AIRLINE", "Community airline", ["Members, roles and invitations", "Shared airline workspace", "Counts toward the one-Community-airline limit"], 0));
            grid.Children.Add(Card("Pro", "AIRLINE", "Pro airline", ["Everything in Community", "Does not use your Community slot", "Shared dispatch and flight uploads (in development)", "Airline branding (in development)"], 2));
        }
        Body.Children.Add(grid);
        Body.Children.Add(new TextBlock { Text = airline is null ? "Changing your level takes effect immediately and can be changed again at any time." : "Only the airline owner can change its level. A fresh identity check is required.", FontSize = 12, Foreground = OpsUi.Brush("#8FA3B1"), Margin = new Thickness(0, 6, 0, 0) });
        Highlight();
    }

    private Border Card(string value, string eyebrow, string title, string[] features, int column)
    {
        var stack = new StackPanel();
        stack.Children.Add(Eyebrow(eyebrow, "#96A6B4"));
        stack.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
        stack.Children.Add(new TextBlock { Text = value == "Free" || value == "Community" ? "INCLUDED" : "COMPLIMENTARY · EARLY ACCESS", FontFamily = new FontFamily("Consolas"), FontSize = 10, Foreground = OpsUi.Brush("#E5C44A"), Margin = new Thickness(0, 0, 0, 14) });
        foreach (var feature in features)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };
            line.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11, Foreground = OpsUi.Brush("#5FAE6E"), Margin = new Thickness(0, 3, 10, 0) });
            line.Children.Add(new TextBlock { Text = feature, FontSize = 13, Foreground = OpsUi.Brush("#C9D4DC") });
            stack.Children.Add(line);
        }
        var card = Boxed(stack); card.Cursor = Cursors.Hand; card.Margin = new Thickness(0);
        System.Windows.Automation.AutomationProperties.SetName(card, title);
        card.MouseLeftButtonUp += (_, _) => { chosen = value; Highlight(); };
        Grid.SetColumn(card, column); cards.Add((card, value));
        return card;
    }

    internal void Choose(string value) { chosen = value; Highlight(); }

    private void Highlight()
    {
        foreach (var (card, value) in cards)
        {
            var selected = value == chosen;
            card.BorderBrush = OpsUi.Brush(selected ? "#E5C44A" : "#2E3F4E"); card.BorderThickness = new Thickness(selected ? 2 : 1);
            card.Effect = selected ? new System.Windows.Media.Effects.DropShadowEffect { Color = (Color)ColorConverter.ConvertFromString("#E5C44A"), BlurRadius = 22, ShadowDepth = 0, Opacity = 0.3 } : null;
        }
    }

    protected override async Task OnPrimaryAsync()
    {
        if (airline is null)
        {
            var plan = Enum.Parse<PersonalPlan>(chosen);
            if (plan != Account.Bootstrap?.PersonalEntitlement.Plan) { await Account.SetPersonalPlanAsync(plan, Lifetime.Token); Changed = true; }
        }
        else
        {
            var plan = Enum.Parse<AirlinePlan>(chosen);
            if (plan != airline.Plan) { await Account.WithStepUpAsync(() => Account.SetAirlinePlanAsync(airline.Id, plan, Lifetime.Token), ConfirmStepUpAsync, Lifetime.Token); Changed = true; }
        }
        Finish();
    }
}

internal sealed class CreateAirlineWindow : PortalDialog
{
    private readonly TextBox name, slug, callsign;
    private bool slugEdited;
    internal AirlineWorkspace? Created { get; private set; }

    internal CreateAirlineWindow(Window owner, DesktopAccountSession account)
        : base(owner, account, "NEW VIRTUAL AIRLINE", "Create your airline", "You become the owner and founder. One Community airline per account; a fresh identity check is required.", "Create airline", 600, 600)
    {
        name = Field("Airline name", "", 100, "Shown to members and on the website.");
        slug = Field("Airline address", "", 64, "Lowercase letters, digits and hyphens. Forms the airline’s web address and cannot be changed later.");
        callsign = Field("Callsign", "", 20, "Radio callsign or ICAO designator, for example NORTHSTAR or NSV.");
        callsign.CharacterCasing = CharacterCasing.Upper;
        name.TextChanged += (_, _) => { if (!slugEdited) slug.Text = Slugify(name.Text); };
        slug.PreviewKeyDown += (_, _) => slugEdited = true;
        Loaded += (_, _) => name.Focus();
    }

    internal static string Slugify(string value)
    {
        var chars = value.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        var collapsed = new string(chars);
        while (collapsed.Contains("--")) collapsed = collapsed.Replace("--", "-");
        collapsed = collapsed.Trim('-');
        return collapsed.Length > 64 ? collapsed[..64].TrimEnd('-') : collapsed;
    }

    protected override async Task OnPrimaryAsync()
    {
        var request = new CreateAirlineRequest(name.Text.Trim(), slug.Text.Trim().ToLowerInvariant(), callsign.Text.Trim().ToUpperInvariant());
        if (request.Name.Length == 0) { SetStatus("Enter an airline name."); name.Focus(); return; }
        if (request.Slug.Length < 3) { SetStatus("The airline address needs at least 3 characters."); slug.Focus(); return; }
        if (request.Callsign.Length == 0) { SetStatus("Enter a callsign."); callsign.Focus(); return; }
        Created = await Account.WithStepUpAsync(() => Account.CreateAirlineAsync(request, Lifetime.Token), ConfirmStepUpAsync, Lifetime.Token);
        Finish();
    }
}

internal sealed class JoinAirlineWindow : PortalDialog
{
    private readonly TextBox code;
    internal AirlineWorkspace? Joined { get; private set; }

    internal JoinAirlineWindow(Window owner, DesktopAccountSession account)
        : base(owner, account, "JOIN A VIRTUAL AIRLINE", "Join with an invite", "Paste the invitation code an airline administrator shared with you. It must have been issued to your verified email address.", "Join airline", 600, 470)
    {
        code = Field("Invitation code", "", 128, "Codes are single use and expire after seven days.");
        var paste = new Button { Content = "Paste from clipboard", Style = (Style)FindResource("PortalLink"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, -6, 0, 0) };
        paste.Click += (_, _) => { if (Clipboard.ContainsText()) code.Text = Clipboard.GetText().Trim(); };
        Body.Children.Add(paste);
        Loaded += (_, _) => code.Focus();
    }

    protected override async Task OnPrimaryAsync()
    {
        var value = code.Text.Trim();
        if (value.Length == 0) { SetStatus("Paste your invitation code."); code.Focus(); return; }
        Joined = await Account.AcceptInvitationAsync(value, Lifetime.Token);
        Finish();
    }
}

// Issues one invitation and shows the raw code exactly once, with the join address to share alongside it.
internal sealed class InviteMemberWindow : PortalDialog
{
    private readonly AirlineWorkspace airline;
    private readonly TextBox email;
    private readonly CheckBox pilot, dispatcher, administrator;
    internal IssuedInvitation? Issued { get; private set; }

    internal InviteMemberWindow(Window owner, DesktopAccountSession account, AirlineWorkspace airline)
        : base(owner, account, airline.Name.ToUpperInvariant(), "Invite a member", "The invitation is tied to this email address. You’ll get a code to share; Alpha 6 does not send email yet.", "Issue invitation", 600, 600)
    {
        this.airline = airline;
        email = Field("Member email", "", 254);
        Body.Children.Add(Caption("Roles", 4));
        pilot = Check("Pilot", true, "Flies for the airline and sees its dispatch board.");
        dispatcher = Check("Dispatcher", false, "Plans and manages airline dispatch.");
        administrator = Check("Administrator", false, "Manages members and invitations. Requires the invitee to confirm with MFA when accepting.");
        Body.Children.Add(new TextBlock { Text = "Ownership is never granted by invitation; use ownership transfer instead.", FontSize = 11, Foreground = OpsUi.Brush("#8FA3B1") });
        Loaded += (_, _) => email.Focus();
    }

    protected override async Task OnPrimaryAsync()
    {
        if (Issued is not null) { Finish(); return; }
        var roles = new List<AirlineRole>();
        if (pilot.IsChecked == true) roles.Add(AirlineRole.Pilot);
        if (dispatcher.IsChecked == true) roles.Add(AirlineRole.Dispatcher);
        if (administrator.IsChecked == true) roles.Add(AirlineRole.Administrator);
        var address = email.Text.Trim();
        if (!System.Net.Mail.MailAddress.TryCreate(address, out var parsed) || parsed.Address != address) { SetStatus("Enter a valid email address."); email.Focus(); return; }
        if (roles.Count == 0) { SetStatus("Choose at least one role."); return; }
        Issued = await Account.WithStepUpAsync(() => Account.InviteAsync(airline.Id, new(address, roles.ToArray()), Lifetime.Token), ConfirmStepUpAsync, Lifetime.Token);
        ShowIssued(Issued);
    }

    private void ShowIssued(IssuedInvitation issued)
    {
        SetHeader("Invitation issued", $"Share this code with {issued.Invitation.Email.ToLowerInvariant()} along with the join address. It is shown once and expires {issued.Invitation.ExpiresAt.ToLocalTime():d MMM yyyy}.");
        Body.Children.Clear();
        Body.Children.Add(Caption("Invitation code"));
        var code = new TextBox { Style = (Style)FindResource("PortalInput"), Text = issued.Token, IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 15 };
        Body.Children.Add(code);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 18) };
        var copy = new Button { Content = "Copy code", Style = (Style)FindResource("PortalButton"), Padding = new Thickness(16, 9, 16, 9), Margin = new Thickness(0, 0, 10, 0) };
        copy.Click += (_, _) => { Clipboard.SetText(issued.Token); SetStatus("Code copied to the clipboard."); };
        var joinUrl = Account.Configuration.PortalUrl.TrimEnd('/') + "/Account/Join";
        var copyAll = new Button { Content = "Copy code and join address", Style = (Style)FindResource("PortalButton"), Padding = new Thickness(16, 9, 16, 9) };
        copyAll.Click += (_, _) => { Clipboard.SetText($"You’re invited to {airline.Name} on Alpha 6 OPS.\nJoin in the desktop app (Join with an invite) or at {joinUrl}\nInvitation code: {issued.Token}"); SetStatus("Invitation text copied to the clipboard."); };
        actions.Children.Add(copy); actions.Children.Add(copyAll); Body.Children.Add(actions);
        Body.Children.Add(Caption("Join address"));
        Body.Children.Add(new TextBlock { Text = joinUrl, FontFamily = new FontFamily("Consolas"), FontSize = 12, Foreground = OpsUi.Brush("#C9D4DC") });
        Body.Children.Add(new TextBlock { Text = $"Roles: {string.Join(", ", issued.Invitation.Roles)}. Re-inviting the same address replaces this code.", FontSize = 12, Foreground = OpsUi.Brush("#8FA3B1"), Margin = new Thickness(0, 14, 0, 0) });
        Cancel.Visibility = Visibility.Collapsed;
        Primary.Content = "Done";
    }
}

// Roster for administrators: members with role editing and ownership transfer, pending invitations with revoke,
// plus airline-level settings for the owner. Every load happens inside one identity check.
internal sealed class AirlineMembersWindow : PortalDialog
{
    private readonly AirlineWorkspace airline;
    private readonly StackPanel membersPanel = new();
    private readonly StackPanel invitationsPanel = new();
    private readonly Guid? selfId;
    internal bool Changed { get; private set; }

    internal AirlineMembersWindow(Window owner, DesktopAccountSession account, AirlineWorkspace airline)
        : base(owner, account, airline.Callsign.ToUpperInvariant() + "  /  MEMBERS", airline.Name, "Members, roles and invitations. Administrative changes stay unlocked for five minutes after an identity check.", "Close", 760, 720)
    {
        this.airline = airline; selfId = account.Bootstrap?.Account.Id;
        Cancel.Visibility = Visibility.Collapsed;
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        toolbar.Children.Add(Tool("Invite member", async () => { var dialog = new InviteMemberWindow(this, Account, airline); dialog.ShowDialog(); if (dialog.Issued is not null) await LoadAsync(); }, primary: true));
        if (airline.Roles.Contains(AirlineRole.Owner))
            toolbar.Children.Add(Tool("Airline level", () => { var dialog = new PlanSelectionWindow(this, Account, airline); dialog.ShowDialog(); if (dialog.Changed) { Changed = true; } return Task.CompletedTask; }));
        toolbar.Children.Add(Tool("Airline logo", async () =>
        {
            var picked = ImagePicker.Pick(this, "Choose a logo for " + airline.Name);
            if (picked is null) return;
            await Account.SetAirlineLogoAsync(airline.Id, picked.Value.Bytes, picked.Value.ContentType, Lifetime.Token);
            Changed = true; SetStatus("Logo updated. It appears on the airline card and the website.");
        }));
        toolbar.Children.Add(Tool("Refresh", LoadAsync));
        Body.Children.Add(toolbar);
        Body.Children.Add(Eyebrow("MEMBERS", "#C9D4DC")); Body.Children.Add(new Border { Height = 8 });
        Body.Children.Add(membersPanel);
        Body.Children.Add(new Border { Height = 10 });
        Body.Children.Add(Eyebrow("PENDING INVITATIONS", "#C9D4DC")); Body.Children.Add(new Border { Height = 8 });
        Body.Children.Add(invitationsPanel);
        Loaded += async (_, _) => await RunAsync(LoadAsync);
    }

    private Button Tool(string label, Func<Task> action, bool primary = false)
    {
        var button = new Button { Content = label, Style = (Style)FindResource(primary ? "PortalPrimary" : "PortalButton"), Padding = new Thickness(16, 9, 16, 9), Margin = new Thickness(0, 0, 10, 0) };
        button.Click += async (_, _) => await RunAsync(action);
        return button;
    }

    internal Task ReloadAsync() => RunAsync(LoadAsync);

    private async Task LoadAsync()
    {
        var (members, invitations) = await Account.WithStepUpAsync(async () =>
            (await Account.ListMembersAsync(airline.Id, Lifetime.Token), await Account.ListInvitationsAsync(airline.Id, Lifetime.Token)), ConfirmStepUpAsync, Lifetime.Token);
        membersPanel.Children.Clear();
        foreach (var member in members.OrderByDescending(m => m.Roles.Contains(AirlineRole.Owner)).ThenBy(m => m.DisplayName)) membersPanel.Children.Add(MemberRow(member));
        invitationsPanel.Children.Clear();
        var pending = invitations.Where(i => i.Status == "pending").ToArray();
        if (pending.Length == 0) invitationsPanel.Children.Add(new TextBlock { Text = "No pending invitations.", FontSize = 13, Foreground = OpsUi.Brush("#8FA3B1") });
        foreach (var invitation in pending) invitationsPanel.Children.Add(InvitationRow(invitation));
    }

    private Border MemberRow(MemberResponse member)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = new StackPanel();
        var name = new StackPanel { Orientation = Orientation.Horizontal };
        name.Children.Add(new TextBlock { Text = member.DisplayName, FontSize = 15, FontWeight = FontWeights.SemiBold });
        if (member.UserId == selfId) name.Children.Add(new TextBlock { Text = "YOU", FontFamily = new FontFamily("Consolas"), FontSize = 10, Foreground = OpsUi.Brush("#E5C44A"), Margin = new Thickness(10, 4, 0, 0) });
        text.Children.Add(name);
        text.Children.Add(new TextBlock { Text = member.Email.ToLowerInvariant(), FontSize = 12, Foreground = OpsUi.Brush("#8FA3B1"), Margin = new Thickness(0, 2, 0, 6) });
        text.Children.Add(Eyebrow(string.Join("  ·  ", member.Roles.Select(r => r.ToString().ToUpperInvariant())) + (member.Status == MembershipStatus.Active ? "" : "  ·  " + member.Status.ToString().ToUpperInvariant()), "#A9B8C4"));
        grid.Children.Add(text);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var isOwner = member.Roles.Contains(AirlineRole.Owner);
        if (!isOwner && member.Status == MembershipStatus.Active)
            actions.Children.Add(Tool("Roles", async () =>
            {
                var dialog = new ChangeRolesWindow(this, Account, airline, member); dialog.ShowDialog();
                if (dialog.Saved) await LoadAsync();
            }));
        if (!isOwner && member.Status == MembershipStatus.Active && airline.Roles.Contains(AirlineRole.Owner) && member.UserId != selfId)
            actions.Children.Add(Tool("Make owner", async () =>
            {
                if (!OpsConfirmWindow.Ask(this, "Transfer ownership", $"{member.DisplayName} becomes the owner of {airline.Name}. You keep administrator access and remain the founder. This cannot be undone from here.", "Keep ownership", "Transfer ownership")) return;
                await Account.WithStepUpAsync(async () => { await Account.TransferOwnershipAsync(airline.Id, member.UserId, Lifetime.Token); return true; }, ConfirmStepUpAsync, Lifetime.Token);
                Changed = true; SetStatus("Ownership transferred. Reopen this airline to manage it as an administrator."); Finish();
            }));
        Grid.SetColumn(actions, 1); grid.Children.Add(actions);
        return Boxed(grid);
    }

    private Border InvitationRow(InvitationResponse invitation)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = invitation.Email.ToLowerInvariant(), FontSize = 14, FontWeight = FontWeights.SemiBold });
        text.Children.Add(Eyebrow($"{string.Join("  ·  ", invitation.Roles.Select(r => r.ToString().ToUpperInvariant()))}   /   EXPIRES {invitation.ExpiresAt.ToLocalTime():d MMM yyyy}", "#A9B8C4"));
        grid.Children.Add(text);
        var revoke = Tool("Revoke", async () => { await Account.WithStepUpAsync(async () => { await Account.RevokeInvitationAsync(airline.Id, invitation.Id, Lifetime.Token); return true; }, ConfirmStepUpAsync, Lifetime.Token); await LoadAsync(); });
        revoke.VerticalAlignment = VerticalAlignment.Center; revoke.Margin = new Thickness(0);
        Grid.SetColumn(revoke, 1); grid.Children.Add(revoke);
        return Boxed(grid, "#26323E");
    }

    protected override Task OnPrimaryAsync() { Finish(); return Task.CompletedTask; }
}

internal sealed class ChangeRolesWindow : PortalDialog
{
    private readonly AirlineWorkspace airline;
    private readonly MemberResponse member;
    private readonly CheckBox pilot, dispatcher, administrator;
    internal bool Saved { get; private set; }

    internal ChangeRolesWindow(Window owner, DesktopAccountSession account, AirlineWorkspace airline, MemberResponse member)
        : base(owner, account, airline.Name.ToUpperInvariant(), $"Roles for {member.DisplayName}", "Changes apply immediately. Ownership is managed separately through transfer.", "Save roles", 560, 470)
    {
        this.airline = airline; this.member = member;
        pilot = Check("Pilot", member.Roles.Contains(AirlineRole.Pilot), "Flies for the airline and sees its dispatch board.");
        dispatcher = Check("Dispatcher", member.Roles.Contains(AirlineRole.Dispatcher), "Plans and manages airline dispatch.");
        administrator = Check("Administrator", member.Roles.Contains(AirlineRole.Administrator), "Manages members and invitations.");
    }

    protected override async Task OnPrimaryAsync()
    {
        var roles = new List<AirlineRole>();
        if (pilot.IsChecked == true) roles.Add(AirlineRole.Pilot);
        if (dispatcher.IsChecked == true) roles.Add(AirlineRole.Dispatcher);
        if (administrator.IsChecked == true) roles.Add(AirlineRole.Administrator);
        if (roles.Count == 0) { SetStatus("Keep at least one role, or remove the member on the website."); return; }
        await Account.WithStepUpAsync(async () => { await Account.ChangeRolesAsync(airline.Id, member.Id, roles.ToArray(), Lifetime.Token); return true; }, ConfirmStepUpAsync, Lifetime.Token);
        Saved = true; Finish();
    }
}

// Placeholder for the future installer/updater: reports the running version and the published release when one exists.
internal sealed class ReleaseWindow : PortalDialog
{
    private readonly TextBlock statusLine = new() { FontSize = 14, LineHeight = 22, Margin = new Thickness(0, 0, 0, 12) };
    private readonly IdentityConfiguration configuration;

    internal ReleaseWindow(Window owner, DesktopAccountSession account)
        : base(owner, account, "DOWNLOAD  /  UPDATES", "Updates", "Automatic updates are coming. Until then, new builds are announced on the website.", "Open download page", 560, 440)
    {
        configuration = account.Configuration;
        Cancel.Content = "Close";
        var version = new StackPanel();
        version.Children.Add(Eyebrow("INSTALLED VERSION", "#C9D4DC"));
        version.Children.Add(new TextBlock { Text = "v" + DesktopAccountSession.ClientVersion, FontSize = 30, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
        Body.Children.Add(Boxed(version));
        Body.Children.Add(statusLine);
        statusLine.Text = "Checking for a published release…";
        Loaded += async (_, _) =>
        {
            var release = await DesktopRelease.CheckAsync(configuration, Lifetime.Token);
            statusLine.Text = release is null ? "No installer has been published yet. This build was installed from a development checkout."
                : release.Version == DesktopAccountSession.ClientVersion ? $"You are on the latest published release (v{release.Version})."
                : $"Release v{release.Version} is available. Automatic download will arrive in a later build; use the download page for now.";
        };
    }

    protected override Task OnPrimaryAsync()
    {
        try { Process.Start(new ProcessStartInfo(configuration.PortalUrl.TrimEnd('/') + "/Download") { UseShellExecute = true }); }
        catch (Exception) { SetStatus("The website could not be opened. Check your default browser and try again."); }
        return Task.CompletedTask;
    }
}

internal static class DesktopRelease
{
    // Anonymous, best-effort: any failure simply means "nothing published".
    internal static async Task<ReleaseInfo?> CheckAsync(IdentityConfiguration configuration, CancellationToken token)
    {
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(configuration.ApiBaseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Alpha6OPS/" + DesktopAccountSession.ClientVersion);
            using var response = await client.GetAsync("api/v1/release", token);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<ReleaseInfo>(cancellationToken: token);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException) { return null; }
    }
}
