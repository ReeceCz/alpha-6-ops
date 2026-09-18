using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;

namespace Alpha6Ops.Server;

public static class AccountApi
{
    private static Task<byte[]> ReadImage(HttpContext c, CancellationToken ct) => AccountApiSupport.ReadImage(c, ct);
    public static void MapAccountApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization("desktop").RequireRateLimiting("requests");
        ActorIdentity Actor(HttpContext context) => TrustedActor.Read(context.User, app.Services.GetRequiredService<ServerSettings>());
        api.MapGet("/me/bootstrap", (HttpContext c, AccountsService s, CancellationToken ct) => s.BootstrapAsync(Actor(c), c.Request.Headers.UserAgent.ToString(), ct));
        api.MapPut("/me/workspace", async (HttpContext c, AccountsService s, WorkspaceSelection request, CancellationToken ct) =>
        { await s.SetWorkspaceAsync(Actor(c), request, ct); return Results.NoContent(); });
        api.MapGet("/me/profile", (HttpContext c, AccountsService s, CancellationToken ct) => s.GetProfileAsync(Actor(c), ct));
        api.MapPut("/me/profile", (HttpContext c, AccountsService s, UpdateProfileRequest request, CancellationToken ct) => s.UpdateProfileAsync(Actor(c), request, ct));
        api.MapPut("/me/plan", (HttpContext c, AccountsService s, SetPersonalPlanRequest request, CancellationToken ct) => s.SetPersonalPlanAsync(Actor(c), request, ct));
        api.MapPut("/virtual-airlines/{id:guid}/plan", (HttpContext c, AccountsService s, Guid id, SetAirlinePlanRequest request, CancellationToken ct) => s.SetAirlinePlanAsync(Actor(c), id, request, ct));
        api.MapPost("/virtual-airlines", (HttpContext c, AccountsService s, CreateAirlineRequest request, CancellationToken ct) => s.CreateAirlineAsync(Actor(c), request, ct));
        api.MapGet("/virtual-airlines/{id:guid}", (HttpContext c, AccountsService s, Guid id, CancellationToken ct) => s.GetAirlineAsync(Actor(c), id, ct));
        api.MapGet("/virtual-airlines/{id:guid}/members", (HttpContext c, AccountsService s, Guid id, CancellationToken ct) => s.ListMembersAsync(Actor(c), id, ct));
        api.MapGet("/virtual-airlines/{id:guid}/invitations", (HttpContext c, AccountsService s, Guid id, CancellationToken ct) => s.ListInvitationsAsync(Actor(c), id, ct));
        api.MapGet("/virtual-airlines/{id:guid}/activity", (HttpContext c, AccountsService s, Guid id, CancellationToken ct) => s.ListActivityAsync(Actor(c), id, ct));
        // Images arrive as the raw request body (image/png, image/jpeg or image/webp); validation is by content.
        api.MapPut("/me/avatar", async (HttpContext c, AccountsService s, CancellationToken ct) => s.SetAvatarAsync(Actor(c), await ReadImage(c, ct), ct));
        api.MapDelete("/me/avatar", async (HttpContext c, AccountsService s, CancellationToken ct) => { await s.RemoveAvatarAsync(Actor(c), ct); return Results.NoContent(); });
        api.MapPut("/virtual-airlines/{id:guid}/logo", async (HttpContext c, AccountsService s, Guid id, CancellationToken ct) => s.SetAirlineLogoAsync(Actor(c), id, await ReadImage(c, ct), ct));
        api.MapDelete("/virtual-airlines/{id:guid}/logo", async (HttpContext c, AccountsService s, Guid id, CancellationToken ct) => { await s.RemoveAirlineLogoAsync(Actor(c), id, ct); return Results.NoContent(); });
        api.MapGet("/me/flights", (HttpContext c, AccountsService s, int page, int pageSize, CancellationToken ct) => s.ListFlightsAsync(Actor(c), page == 0 ? 1 : page, pageSize == 0 ? 50 : pageSize, ct));
        api.MapGet("/me/flights/summary", (HttpContext c, AccountsService s, CancellationToken ct) => s.LogbookSummaryAsync(Actor(c), ct));
        api.MapPost("/me/flights", (HttpContext c, AccountsService s, FlightLogRequest request, CancellationToken ct) => s.AddFlightAsync(Actor(c), request, "desktop", ct));
        api.MapPost("/me/flights/import", (HttpContext c, AccountsService s, LogbookImportRequest request, CancellationToken ct) => s.ImportLogbookAsync(Actor(c), request, ct));
        api.MapDelete("/me/flights/imports/{batchId:guid}", async (HttpContext c, AccountsService s, Guid batchId, CancellationToken ct) => Results.Ok(new { removed = await s.UndoImportAsync(Actor(c), batchId, ct) }));
        api.MapDelete("/me/flights/{flightId:guid}", async (HttpContext c, AccountsService s, Guid flightId, CancellationToken ct) =>
        { await s.DeleteFlightAsync(Actor(c), flightId, ct); return Results.NoContent(); });
        api.MapGet("/virtual-airlines/{id:guid}/routes", (HttpContext c, AccountsService s, Guid id, CancellationToken ct) => s.ListRoutesAsync(Actor(c), id, ct));
        api.MapPost("/virtual-airlines/{id:guid}/routes", (HttpContext c, AccountsService s, Guid id, RouteRequest request, CancellationToken ct) => s.SaveRouteAsync(Actor(c), id, null, request, ct));
        api.MapPut("/virtual-airlines/{id:guid}/routes/{routeId:guid}", (HttpContext c, AccountsService s, Guid id, Guid routeId, RouteRequest request, CancellationToken ct) => s.SaveRouteAsync(Actor(c), id, routeId, request, ct));
        api.MapDelete("/virtual-airlines/{id:guid}/routes/{routeId:guid}", async (HttpContext c, AccountsService s, Guid id, Guid routeId, CancellationToken ct) =>
        { await s.DeleteRouteAsync(Actor(c), id, routeId, ct); return Results.NoContent(); });
        api.MapPost("/virtual-airlines/{id:guid}/routes/import", (HttpContext c, AccountsService s, Guid id, ScheduleImportRequest request, CancellationToken ct) => s.ImportRoutesAsync(Actor(c), id, request, ct));
        api.MapGet("/virtual-airlines/{id:guid}/fleet", (HttpContext c, AccountsService s, Guid id, CancellationToken ct) => s.ListFleetAsync(Actor(c), id, ct));
        api.MapPost("/virtual-airlines/{id:guid}/fleet", (HttpContext c, AccountsService s, Guid id, AircraftRequest request, CancellationToken ct) => s.SaveAircraftAsync(Actor(c), id, null, request, ct));
        api.MapPut("/virtual-airlines/{id:guid}/fleet/{aircraftId:guid}", (HttpContext c, AccountsService s, Guid id, Guid aircraftId, AircraftRequest request, CancellationToken ct) => s.SaveAircraftAsync(Actor(c), id, aircraftId, request, ct));
        api.MapDelete("/virtual-airlines/{id:guid}/fleet/{aircraftId:guid}", async (HttpContext c, AccountsService s, Guid id, Guid aircraftId, CancellationToken ct) =>
        { await s.DeleteAircraftAsync(Actor(c), id, aircraftId, ct); return Results.NoContent(); });
        api.MapPost("/virtual-airlines/{id:guid}/invitations", (HttpContext c, AccountsService s, Guid id, InviteMemberRequest request, CancellationToken ct) => s.InviteAsync(Actor(c), id, request, ct));
        api.MapPost("/invitations/accept", (HttpContext c, AccountsService s, AcceptInvitationBody request, CancellationToken ct) => s.AcceptInvitationAsync(Actor(c), request.Token, ct));
        api.MapDelete("/virtual-airlines/{id:guid}/invitations/{invitationId:guid}", async (HttpContext c, AccountsService s, Guid id, Guid invitationId, CancellationToken ct) =>
        { await s.RevokeInvitationAsync(Actor(c), id, invitationId, ct); return Results.NoContent(); });
        api.MapPut("/virtual-airlines/{id:guid}/members/{membershipId:guid}/roles", async (HttpContext c, AccountsService s, Guid id, Guid membershipId, ChangeRolesRequest request, CancellationToken ct) =>
        { await s.ChangeRolesAsync(Actor(c), id, membershipId, request, ct); return Results.NoContent(); });
        api.MapPost("/virtual-airlines/{id:guid}/ownership-transfer", async (HttpContext c, AccountsService s, Guid id, TransferOwnershipRequest request, CancellationToken ct) =>
        { await s.TransferOwnershipAsync(Actor(c), id, request, ct); return Results.NoContent(); });
    }
}

public sealed record AcceptInvitationBody(string Token);

public static partial class AccountApiSupport
{
    public static async Task<byte[]> ReadImage(HttpContext context, CancellationToken ct)
    {
        if (context.Request.ContentLength is > ImageRules.MaxBytes) throw new IdentityException("invalid_image", "Images must be 1 MB or smaller.", 413);
        using var buffer = new MemoryStream();
        var chunk = new byte[16384]; int read;
        while ((read = await context.Request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > ImageRules.MaxBytes) throw new IdentityException("invalid_image", "Images must be 1 MB or smaller.", 413);
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
