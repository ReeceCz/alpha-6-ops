using System.Net.Mail;

namespace Alpha6Ops.Server;

/// <summary>Who runs the service, as stated on the Privacy and Terms pages.</summary>
public static class Legal
{
    public const string Operator = "Sierra 4";
    public const string OperatorDescription = "Sierra 4 is an unincorporated project run by Reece Czarnecki and Dan Iwanski.";
    public const string Updated = "13 October 2026";
    public const int DeletionGraceDays = Alpha6Ops.Accounts.AccountRules.DeletionGraceDays;
}

/// <summary>
/// The public contact address and governing law, from the <c>Legal</c> section (<c>Legal__Contact</c>,
/// <c>Legal__Jurisdiction</c> on Render), so they change without a release. Until a valid contact address is set,
/// pages say how to reach the team instead of showing a placeholder or a broken <c>mailto:</c>.
/// </summary>
public sealed class LegalSettings
{
    public string Contact { get; set; } = "";
    public string Jurisdiction { get; set; } = "Wisconsin, United States";
    public bool HasContact => MailAddress.TryCreate(Contact, out var parsed) && parsed.Address == Contact;
}
