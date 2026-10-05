using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Alpha6Ops.Server.Pages.Account;

[RequestSizeLimit(5_600_000), RequestFormLimits(MultipartBodyLengthLimit = 5_600_000)]
public class FeedbackModel(AccountsService accounts, ServerSettings settings, Mailer mailer) : AccountPage(accounts, settings)
{
    [BindProperty] public FeedbackCategory Category { get; set; } = FeedbackCategory.FlightRecording;
    [BindProperty] public string? Message { get; set; }
    [BindProperty] public string? FlightNumber { get; set; }
    [BindProperty] public DateOnly? FlownOn { get; set; }
    [BindProperty] public IFormFile? Attachment { get; set; }
    public FeedbackView[] Reports { get; private set; } = [];
    public bool Sent { get; private set; }

    public static IReadOnlyList<(FeedbackCategory Value, string Label, string Hint)> Categories { get; } =
    [
        (FeedbackCategory.FlightRecording, "A flight wasn't recorded right", "A missing or wrong OUT, OFF, ON or IN time, or a flight that didn't log."),
        (FeedbackCategory.Bug, "Something is broken", "An error, a crash, or a screen that doesn't work."),
        (FeedbackCategory.Idea, "An idea", "Something that would make S4 OPS better for you."),
        (FeedbackCategory.Other, "Something else", "")
    ];
    public static string Label(FeedbackCategory c) => Categories.First(x => x.Value == c).Label;

    public async Task<IActionResult> OnGetAsync(bool sent, CancellationToken ct)
    {
        try
        {
            Reports = await Accounts.ListMyFeedbackAsync(Actor, ct);
            await Accounts.MarkMyFeedbackReadAsync(Actor, ct); // the replies are on screen now
            Sent = sent;
            return Page();
        }
        catch (IdentityException ex) { return Failure(ex); }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        try
        {
            FeedbackFile? file = null;
            if (Attachment is { Length: > 0 })
            {
                using var buffer = new MemoryStream();
                await Attachment.CopyToAsync(buffer, ct);
                file = new(Attachment.FileName, buffer.ToArray());
            }
            var report = await Accounts.SubmitFeedbackAsync(Actor, new(Category, Message, FlightNumber, FlownOn), file, ct);
            mailer.NewReport(Actor.DisplayName, Label(report.Category), report.Id);
            return RedirectToPage(new { sent = true });
        }
        catch (IdentityException ex)
        {
            try { Reports = await Accounts.ListMyFeedbackAsync(Actor, ct); } catch (IdentityException) { }
            return Failure(ex);
        }
    }
}
