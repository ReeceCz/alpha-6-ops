using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Alpha6Ops.Server.Pages.Account;

public abstract class AccountPage(AccountsService accounts, ServerSettings settings) : PageModel
{
    protected AccountsService Accounts { get; } = accounts;
    protected ActorIdentity Actor => TrustedActor.Read(User, settings);
    public string? ErrorMessage { get; protected set; }
    public bool NeedsVerification { get; protected set; }
    public string? ErrorCode { get; protected set; }
    /// <summary>The signed-in email, shown when the account isn't admitted yet so its invitation request is one click.</summary>
    public string? ActorEmail { get; private set; }
    /// <summary>That email already has a request waiting for staff.</summary>
    public bool RequestPending { get; protected set; }
    protected IActionResult Failure(IdentityException error)
    {
        // A locked account can only see its deletion, cancel it, or download its data.
        if (error.Code == "account_pending_deletion") return Redirect("/Account/Delete");
        ErrorMessage = error.Message;
        ErrorCode = error.Code;
        if (error.Code == "invite_required") ActorEmail = Actor.Email;
        NeedsVerification = error.Code is "mfa_required" or "recent_authentication_required" or "recent_mfa_required";
        Response.StatusCode = error.StatusCode;
        return Page();
    }
}
