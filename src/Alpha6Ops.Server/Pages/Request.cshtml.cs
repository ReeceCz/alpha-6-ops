using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Alpha6Ops.Server.Pages;

// Public invitation request form. The answer is the same whether or not the email is already known.
public class RequestModel(IServiceProvider services, IOptions<TurnstileSettings> turnstile, InviteRequestLimiter limiter, ITurnstileVerifier verifier, Mailer mailer, ServerSettings settings) : PageModel
{
    [BindProperty] public string? Email { get; set; }
    [BindProperty] public string? Name { get; set; }
    [BindProperty] public string? Note { get; set; }
    // Honeypot: hidden from people, filled in by naive bots.
    [BindProperty(Name = "website")] public string? Website { get; set; }
    [BindProperty(Name = "cf-turnstile-response")] public string? TurnstileToken { get; set; }

    public bool Sent { get; private set; }
    /// <summary>Sent from the account page while signed in: the pilot only has to come back once approved.</summary>
    public bool FromAccount { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? SiteKey => turnstile.Value.Enabled ? turnstile.Value.SiteKey : null;
    public bool Available => services.GetService<AccountsService>() is not null;

    public IActionResult OnGet(bool sent, bool account)
    {
        Sent = sent;
        FromAccount = sent && account;
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

    // From the invite-only notice on the account page. The address is the signed-in one, so there is nothing to type
    // and no bot check; the per-address limit still applies.
    public async Task<IActionResult> OnPostSignedInAsync(string? note, CancellationToken ct)
    {
        AllowTurnstile();
        if (User.Identity?.IsAuthenticated != true) return RedirectToPage();
        if (services.GetService<AccountsService>() is not { } accounts) return Error("Requests aren't available on this installation.", 503);
        if (!limiter.TryAcquire(HttpContext.Connection.RemoteIpAddress?.ToString())) return Error("You've sent a few requests already. Please try again in an hour.", 429);
        try
        {
            var actor = TrustedActor.Read(User, settings);
            var name = actor.DisplayName.Length > 100 ? actor.DisplayName[..100] : actor.DisplayName;
            if (await accounts.SubmitInviteRequestAsync(new(actor.Email, name, note), ct)) mailer.NewRequest(actor.Email.Trim().ToLowerInvariant());
            return RedirectToPage(new { sent = true, account = true });
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
