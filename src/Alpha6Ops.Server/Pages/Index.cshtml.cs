using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Alpha6Ops.Server.Pages;

// The portal's front page is an entrance: signed-in people go straight to their account; the product story lives on s4ops.com.
public sealed class IndexModel : PageModel
{
    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true ? Redirect("/Account") : Page();
}
