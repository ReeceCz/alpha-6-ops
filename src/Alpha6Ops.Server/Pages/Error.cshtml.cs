using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;

namespace Alpha6Ops.Server.Pages;
[AllowAnonymous, IgnoreAntiforgeryToken, ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class ErrorModel(ILogger<ErrorModel> logger, IWebHostEnvironment environment) : PageModel
{
    /// <summary>Quoted in a problem report so staff can find the matching log line. Reveals nothing about the failure.</summary>
    public string Reference { get; private set; } = "";
    /// <summary>Development only: the database is older than the code, which a migration fixes.</summary>
    public bool DatabaseBehind { get; private set; }

    public void OnGet() => Show();
    public void OnPost() => Show();

    private void Show()
    {
        Response.StatusCode = 500;
        Reference = HttpContext.TraceIdentifier;
        var failure = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        logger.LogError("Error page shown for {Path}, reference {Reference}", failure?.Path ?? "(direct)", Reference);
        // 42P01: undefined table, 42703: undefined column. Hosted servers migrate before serving, so only a local run gets here.
        var database = failure?.Error as PostgresException ?? failure?.Error?.InnerException as PostgresException;
        DatabaseBehind = environment.IsDevelopment() && database?.SqlState is "42P01" or "42703";
    }
}
