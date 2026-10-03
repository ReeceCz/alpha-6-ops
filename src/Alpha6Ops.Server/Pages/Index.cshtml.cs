using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Alpha6Ops.Server.Pages;

// The portal's front page is an entrance: signed-in people go straight to their account; the product story lives on s4ops.com.
// Without account services configured, /Account answers 503, so a leftover session cookie stays on the entrance.
public sealed class IndexModel(ServerSettings settings) : PageModel
{
    public IActionResult OnGet() => settings.IsConfigured && User.Identity?.IsAuthenticated == true ? Redirect("/Account") : Page();
}
