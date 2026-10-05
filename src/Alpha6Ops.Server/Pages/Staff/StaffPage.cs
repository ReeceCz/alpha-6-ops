using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;
using Alpha6Ops.Server.Pages.Account;

namespace Alpha6Ops.Server.Pages.Staff;

// Staff pages reuse the account error handling, but "not found" really answers 404 so the pages stay hidden.
public abstract class StaffPage(AccountsService accounts, ServerSettings settings, StaffAccess staff) : AccountPage(accounts, settings)
{
    protected StaffAccess Staff { get; } = staff;
    protected IActionResult StaffFailure(IdentityException error) => error.Code == "not_found" ? NotFound() : Failure(error);
}
