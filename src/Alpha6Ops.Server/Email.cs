using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Alpha6Ops.Accounts;
using Microsoft.Extensions.Options;

namespace Alpha6Ops.Server;

public sealed record EmailMessage(string[] To, string Subject, string Text, string Html, string From, string? ReplyTo);

/// <summary>Email through Resend (https://resend.com/docs/api-reference/emails/send-email). Off until a key is set.</summary>
public sealed class EmailSettings
{
    public string ResendApiKey { get; set; } = "";
    public string From { get; set; } = "S4 OPS <noreply@s4ops.com>";
    /// <summary>Where links in emails point. Emails can be sent from background work, so this isn't taken from a request.</summary>
    public string BaseUrl { get; set; } = "https://app.s4ops.com";
    public bool Enabled => !string.IsNullOrWhiteSpace(ResendApiKey);
}

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

public sealed class ResendEmailSender(HttpClient http, IOptions<EmailSettings> settings, ILogger<ResendEmailSender> log) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = JsonContent.Create(new ResendEmail(message.From, message.To, message.Subject, message.Text, message.Html, message.ReplyTo))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Value.ResendApiKey);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            log.LogWarning("Resend refused an email ({Status}): {Subject}", (int)response.StatusCode, message.Subject);
    }

    private sealed record ResendEmail(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("reply_to"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReplyTo);
}

/// <summary>Used while email is off: records that an email would have gone out, without any address.</summary>
public sealed class LogEmailSender(ILogger<LogEmailSender> log) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        log.LogInformation("Email is off (no Email__ResendApiKey): not sending \"{Subject}\" to {Count} recipient(s).", message.Subject, message.To.Length);
        return Task.CompletedTask;
    }
}

/// <summary>Pages drop messages here and carry on; the dispatcher sends them in the background.</summary>
public sealed class EmailOutbox(ILogger<EmailOutbox> log)
{
    private readonly Channel<EmailMessage> channel = Channel.CreateBounded<EmailMessage>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropWrite });
    public ChannelReader<EmailMessage> Reader => channel.Reader;

    public void Enqueue(EmailMessage message)
    {
        if (message.To.Length == 0) return;
        if (!channel.Writer.TryWrite(message)) log.LogWarning("Email outbox is full; dropped \"{Subject}\".", message.Subject);
    }
}

public sealed class EmailDispatcher(EmailOutbox outbox, IServiceProvider services, ILogger<EmailDispatcher> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in outbox.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    // Resolved per message: the Resend sender is a typed HttpClient with a short lifetime.
                    using var scope = services.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(message, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A notification that can't be sent must never break anything else.
                    log.LogWarning(ex, "Could not send \"{Subject}\".", message.Subject);
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}

/// <summary>The emails S4 OPS sends. Links only: reply text and report contents stay on the site.</summary>
public sealed class Mailer(EmailOutbox outbox, IOptions<EmailSettings> email, IOptions<LegalSettings> legal, StaffAccess staff)
{
    private string Base => email.Value.BaseUrl.TrimEnd('/');
    private string? ReplyTo => legal.Value.HasContact ? legal.Value.Contact : null;

    public void TesterInvited(string to) => Send([to], "You're invited to test S4 OPS",
        $"You're invited to test S4 OPS, airline operations for Microsoft Flight Simulator 2024.\n\n" +
        $"1. Create your account with this email address: {Base}/auth/signup\n" +
        $"2. Verify your email, then sign in.\n" +
        $"3. Read the tester guide, then download the app: {Base}/Guide\n\n" +
        "Something wrong? Use \"Report a problem\" in your account.",
        [("Create your account", $"{Base}/auth/signup"), ("Read the tester guide", $"{Base}/Guide")]);

    public void ReportReplied(string to) => Send([to], "The Sierra 4 team replied to your report",
        $"The Sierra 4 team replied to a problem report you sent.\n\nRead it in your account: {Base}/Account/Feedback",
        [("Read the reply", $"{Base}/Account/Feedback")]);

    public void NewReport(string reporter, string kind, Guid id) => Send(staff.Emails.ToArray(), $"New problem report: {kind}",
        $"{reporter} sent a problem report ({kind}).\n\nOpen it: {Base}/Staff/Feedback/{id}",
        [("Open the report", $"{Base}/Staff/Feedback/{id}")]);

    public void NewRequest(string requester) => Send(staff.Emails.ToArray(), "New invitation request",
        $"{requester} asked for an invitation to the S4 OPS test.\n\nReview requests: {Base}/Staff/Testers#requests",
        [("Review requests", $"{Base}/Staff/Testers#requests")]);

    private void Send(string[] to, string subject, string text, (string Label, string Url)[] links)
    {
        var paragraphs = text.Split("\n\n").Select(p => "<p>" + WebUtility.HtmlEncode(p).Replace("\n", "<br>") + "</p>");
        var buttons = links.Select(l => $"<p><a href=\"{WebUtility.HtmlEncode(l.Url)}\">{WebUtility.HtmlEncode(l.Label)}</a></p>");
        outbox.Enqueue(new EmailMessage(to, subject, text, string.Concat(paragraphs) + string.Concat(buttons), email.Value.From, ReplyTo));
    }
}
