using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Alpha6Ops.Server.Pages.Account;

public class ProfileModel(AccountsService accounts, ServerSettings settings) : AccountPage(accounts, settings)
{
    // Optional fields are nullable so MVC does not treat a blank submission as a missing required value.
    [BindProperty] public string? SimBriefUsername { get; set; }
    [BindProperty] public string? Callsign { get; set; }
    [BindProperty] public string? HomeBaseIcao { get; set; }
    [BindProperty] public string WeightUnit { get; set; } = "LBS";
    [BindProperty] public string AltitudeUnit { get; set; } = "FT";
    [BindProperty] public string LandingDistanceUnit { get; set; } = "FT";
    [BindProperty] public string PreferredWorkspace { get; set; } = "last_used";
    [BindProperty] public string? TimeZone { get; set; }
    [BindProperty] public string? AvatarInitials { get; set; }
    public UserProfile? Profile { get; private set; }
    public bool Saved { get; private set; }
    public static IReadOnlyList<(string Id, string Name)> TimeZones { get; } =
        [("", "Use the device time zone"), .. TimeZoneInfo.GetSystemTimeZones().Select(z => (z.Id, z.DisplayName))];

    public async Task<IActionResult> OnGetAsync(bool saved, CancellationToken ct)
    {
        try { Fill(await Accounts.GetProfileAsync(Actor, ct)); Saved = saved; return Page(); }
        catch (IdentityException ex) { return Failure(ex); }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) return await OnGetAsync(false, ct);
        try
        {
            Fill(await Accounts.UpdateProfileAsync(Actor, new(SimBriefUsername ?? "", Callsign ?? "", HomeBaseIcao ?? "", WeightUnit, AltitudeUnit, LandingDistanceUnit, PreferredWorkspace, TimeZone ?? "", AvatarInitials ?? ""), ct));
            return RedirectToPage(new { saved = true });
        }
        catch (IdentityException ex) { Profile = UserProfile.Default; return Failure(ex); }
    }

    private void Fill(UserProfile profile)
    {
        Profile = profile;
        SimBriefUsername = profile.SimBriefUsername; Callsign = profile.Callsign; HomeBaseIcao = profile.HomeBaseIcao;
        WeightUnit = profile.WeightUnit; AltitudeUnit = profile.AltitudeUnit; LandingDistanceUnit = profile.LandingDistanceUnit;
        PreferredWorkspace = profile.PreferredWorkspace; TimeZone = profile.TimeZone; AvatarInitials = profile.AvatarInitials;
    }
}
