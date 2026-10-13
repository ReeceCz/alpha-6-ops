using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Alpha6Ops.Server.Pages;
/// <summary>Shown in place of an empty error response from a page (not the API): a missing page, an expired form, no access.</summary>
[AllowAnonymous, IgnoreAntiforgeryToken, ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class StatusModel : PageModel
{
    [BindProperty(SupportsGet = true)] public int Code { get; set; }

    public void OnGet() => Show();
    public void OnPost() => Show();

    private void Show()
    {
        if (Code is < 400 or > 599) Code = 404;
        Response.StatusCode = Code;
    }
}
