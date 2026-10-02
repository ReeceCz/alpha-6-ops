using Alpha6Ops.Identity;

namespace Alpha6Ops.Accounts;

/// <summary>
/// Decides whether a first sign-in may create an account. It never affects an account that already exists.
/// Invite-only admits a listed email only when the identity provider has verified it, so nobody can claim
/// an invited address by signing up with it unverified.
/// </summary>
public sealed class AccountAdmission
{
    private readonly HashSet<string>? invited;
    private AccountAdmission(HashSet<string>? invited) => this.invited = invited;

    public static AccountAdmission Open { get; } = new(null);
    public bool IsInviteOnly => invited is not null;

    /// <summary>Semicolon- or comma-separated emails. Empty or missing admits nobody new.</summary>
    public static AccountAdmission InviteOnly(string? emails) => new(new HashSet<string>(
        (emails ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        StringComparer.OrdinalIgnoreCase));

    /// <summary>Null when a new account may be created; otherwise the refusal to report.</summary>
    public IdentityException? Admits(string email, bool emailVerified)
    {
        if (invited is null) return null;
        if (!invited.Contains(email.Trim()))
            return new("invite_required", "S4 OPS is invite-only while we test with a small group of pilots. Ask us for an invitation, then sign in with the invited email address.", 403);
        if (!emailVerified)
            return new("invite_email_unverified", "Verify your email address using the link we sent you, then sign in again.", 403);
        return null;
    }
}
