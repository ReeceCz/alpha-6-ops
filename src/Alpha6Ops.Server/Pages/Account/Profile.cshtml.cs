using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Alpha6Ops.Server.Pages.Account;

[RequestSizeLimit(1_400_000), RequestFormLimits(MultipartBodyLengthLimit = 1_400_000)]
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
    public string? Saved { get; private set; }
    public static IReadOnlyList<(string Id, string Name)> TimeZones { get; } =
        [("", "Use the device time zone"), .. TimeZoneInfo.GetSystemTimeZones().Select(z => (z.Id, z.DisplayName))];

    public async Task<IActionResult> OnGetAsync(string? saved, CancellationToken ct)
    {
        try { Fill(await Accounts.GetProfileAsync(Actor, ct)); Saved = saved; return Page(); }
        catch (IdentityException ex) { return Failure(ex); }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) return await OnGetAsync(null, ct);
        try
        {
            Fill(await Accounts.UpdateProfileAsync(Actor, new(SimBriefUsername ?? "", Callsign ?? "", HomeBaseIcao ?? "", WeightUnit, AltitudeUnit, LandingDistanceUnit, PreferredWorkspace, TimeZone ?? "", AvatarInitials ?? ""), ct));
            return RedirectToPage(new { saved = "Profile saved. The desktop app applies it the next time it refreshes your account." });
        }
        catch (IdentityException ex) { Profile = UserProfile.Default; return Failure(ex); }
    }

    public async Task<IActionResult> OnPostAvatarAsync(IFormFile? avatar, CancellationToken ct)
    {
        try
        {
            if (avatar is null || avatar.Length == 0) throw new IdentityException("invalid_image", "Choose an image file.", 400);
            if (avatar.Length > ImageRules.MaxBytes) throw new IdentityException("invalid_image", "Images must be 1 MB or smaller.", 400);
            using var stream = new MemoryStream();
            await avatar.CopyToAsync(stream, ct);
            await Accounts.SetAvatarAsync(Actor, stream.ToArray(), ct);
            return RedirectToPage(new { saved = "Profile picture updated." });
        }
        catch (IdentityException ex) { Fill(await Accounts.GetProfileAsync(Actor, ct)); return Failure(ex); }
    }

    public async Task<IActionResult> OnPostRemoveAvatarAsync(CancellationToken ct)
    {
        try { await Accounts.RemoveAvatarAsync(Actor, ct); return RedirectToPage(new { saved = "Profile picture removed." }); }
        catch (IdentityException ex) { Fill(await Accounts.GetProfileAsync(Actor, ct)); return Failure(ex); }
    }

    private void Fill(UserProfile profile)
    {
        Profile = profile;
        SimBriefUsername = profile.SimBriefUsername; Callsign = profile.Callsign; HomeBaseIcao = profile.HomeBaseIcao;
        WeightUnit = profile.WeightUnit; AltitudeUnit = profile.AltitudeUnit; LandingDistanceUnit = profile.LandingDistanceUnit;
        PreferredWorkspace = profile.PreferredWorkspace; TimeZone = profile.TimeZone; AvatarInitials = profile.AvatarInitials;
    }
}
