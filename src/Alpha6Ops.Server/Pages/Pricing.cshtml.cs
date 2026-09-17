using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Alpha6Ops.Server.Pages;

// Public tier catalogue for the front door. Prices are placeholders until billing exists; the
// authoritative prices will live in the payment provider, not here.
public class PricingModel : PageModel
{
    public sealed record Tier(string Code, string Group, string Name, string Summary, string[] Included, string Monthly, string Yearly, bool Paid);

    public static IReadOnlyList<Tier> Tiers { get; } =
    [
        new("FREE", "Personal", "Flying as a Pilot", "Everything a solo pilot needs, free.",
            ["Personal logbook and flight tracking", "Dispatch with SimBrief import", "Weather briefings", "Join any number of airlines", "Found one Community airline"],
            "Free", "Free", false),
        new("PREM", "Personal", "Premium pilot", "More history, more tools, sooner.",
            ["Everything in Flying as a Pilot", "Cloud logbook sync across PCs", "Extended flight history", "Early access to new flight tools", "Priority support"],
            "$4.99", "$49", true),
        new("COMM", "Airline", "Community airline", "Run a crew of friends at no cost.",
            ["Crew roster and roles", "Invitation codes", "Shared dispatch board", "One per owner"],
            "Included", "Included", false),
        new("PRO", "Airline", "Pro airline", "For operators running a serious schedule.",
            ["Everything in Community", "Unlimited airlines per owner", "Operations reporting", "Priority support for your crew"],
            "$19.99", "$199", true)
    ];

    public bool SignedIn => User.Identity?.IsAuthenticated == true;
}
