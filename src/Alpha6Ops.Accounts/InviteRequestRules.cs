using Alpha6Ops.Identity;

namespace Alpha6Ops.Accounts;

public sealed record InviteRequestForm(string? Email, string? Name, string? Note);
public sealed record InviteRequestView(Guid Id, string Email, string Name, string Note, DateTimeOffset CreatedAt);

public static class InviteRequestRules
{
    public const int MaxPending = 500;
    public const int RetentionDays = 90;

    public static InviteRequestForm Request(InviteRequestForm form)
    {
        static IdentityException Invalid(string message) => new("invalid_request", message, 400);
        var email = AccountRules.Email(form.Email ?? "");
        var name = (form.Name ?? "").Trim();
        if (name.Length > 100 || name.Any(char.IsControl)) throw Invalid("Keep your name to one line of up to 100 characters.");
        var note = (form.Note ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (note.Length > 1000 || note.Any(c => char.IsControl(c) && c is not '\n' and not '\t')) throw Invalid("Keep the note to 1000 characters.");
        return new(email, name, note);
    }
}
