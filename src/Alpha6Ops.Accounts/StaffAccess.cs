using Alpha6Ops.Identity;

namespace Alpha6Ops.Accounts;

/// <summary>
/// Who may read problem reports: an allowlist of emails from configuration (Staff__Emails). Only a verified
/// email counts. Callers add a recent-MFA check; this is not a role and grants nothing else.
/// </summary>
public sealed class StaffAccess
{
    private readonly HashSet<string> emails;
    private StaffAccess(HashSet<string> emails) => this.emails = emails;

    public static StaffAccess None { get; } = new([]);
    public bool IsConfigured => emails.Count > 0;
    public IReadOnlyCollection<string> Emails => emails;

    /// <summary>Semicolon- or comma-separated emails. Empty or missing means no staff.</summary>
    public static StaffAccess FromEmails(string? list) => new(new HashSet<string>(
        (list ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        StringComparer.OrdinalIgnoreCase));

    public bool IsStaff(ActorIdentity actor) => actor.EmailVerified && emails.Contains((actor.Email ?? "").Trim());
}
