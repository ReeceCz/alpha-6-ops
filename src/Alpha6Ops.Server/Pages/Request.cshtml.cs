using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Alpha6Ops.Server.Pages;

// Public invitation request form. The answer is the same whether or not the email is already known.
public class RequestModel(IServiceProvider services, IOptions<TurnstileSettings> turnstile, InviteRequestLimiter limiter, ITurnstileVerifier verifier, Mailer mailer) : PageModel
{
    [BindProperty] public string? Email { get; set; }
    [BindProperty] public string? Name { get; set; }
    [BindProperty] public string? Note { get; set; }
    // Honeypot: hidden from people, filled in by naive bots.
    [BindProperty(Name = "website")] public string? Website { get; set; }
    [BindProperty(Name = "cf-turnstile-response")] public string? TurnstileToken { get; set; }

    public bool Sent { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? SiteKey => turnstile.Value.Enabled ? turnstile.Value.SiteKey : null;
    public bool Available => services.GetService<AccountsService>() is not null;

    public IActionResult OnGet(bool sent)
    {
        Sent = sent;
        AllowTurnstile();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        AllowTurnstile();
        if (services.GetService<AccountsService>() is not { } accounts) return Error("Requests aren't available on this installation.", 503);
        var address = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (!limiter.TryAcquire(address)) return Error("You've sent a few requests already. Please try again in an hour.", 429);
        if (!string.IsNullOrEmpty(Website)) return RedirectToPage(new { sent = true });
        if (turnstile.Value.Enabled && !await verifier.VerifyAsync(TurnstileToken, address, ct))
            return Error("Please complete the security check and send the request again.", 400);
        try
        {
            if (await accounts.SubmitInviteRequestAsync(new(Email, Name, Note), ct)) mailer.NewRequest((Email ?? "").Trim().ToLowerInvariant());
            return RedirectToPage(new { sent = true });
        }
        catch (IdentityException ex) { return Error(ex.Message, ex.StatusCode); }
    }

    private PageResult Error(string message, int status)
    {
        ErrorMessage = message;
        Response.StatusCode = status;
        return Page();
    }

    // The portal is script-free; with Turnstile on, this page alone may load Cloudflare's widget.
    private void AllowTurnstile()
    {
        if (!turnstile.Value.Enabled) return;
        var csp = Response.Headers.ContentSecurityPolicy.ToString();
        Response.Headers.ContentSecurityPolicy = csp.Replace("script-src 'none'", $"script-src {TurnstileSettings.Origin}") + $"; frame-src {TurnstileSettings.Origin}";
    }
}
