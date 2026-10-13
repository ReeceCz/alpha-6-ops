using Alpha6Ops.Identity;

namespace Alpha6Ops.Server.Pages.Account;

/// <summary>The level names the Levels and Account level pages use, so the account page never shows the enum.</summary>
public static class Levels
{
    public static string Personal(PersonalPlan plan) => plan == PersonalPlan.Premium ? "Premium pilot" : "Flying as a Pilot";
}
