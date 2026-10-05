using System.Text.RegularExpressions;
using Alpha6Ops.Identity;

namespace Alpha6Ops.Accounts;

public sealed record ReleaseNoteRequest(string? Version, string? Title, string? Notes);
public sealed record ReleaseNoteView(string Version, string Title, string Notes, DateTimeOffset PublishedAt, DateTimeOffset UpdatedAt, string Author);

/// <summary>Release notes are plain text: shown with line breaks kept, never interpreted as markup.</summary>
public static partial class ReleaseRules
{
    public static ReleaseNoteRequest Note(ReleaseNoteRequest request)
    {
        static IdentityException Invalid(string message) => new("invalid_release", message, 400);
        var version = (request.Version ?? "").Trim();
        if (!VersionPattern().IsMatch(version)) throw Invalid("Use the desktop version number, for example 0.23.0.");
        var title = (request.Title ?? "").Trim();
        if (title.Length is 0 or > 120 || title.Any(char.IsControl)) throw Invalid("Give the release a one-line title of up to 120 characters.");
        var notes = (request.Notes ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (notes.Length is 0 or > 8000) throw Invalid("Write the notes in 1 to 8000 characters.");
        if (notes.Any(c => char.IsControl(c) && c is not '\n' and not '\t')) throw Invalid("The notes contain a character that can't be shown.");
        return new(version, title, notes);
    }

    [GeneratedRegex(@"^\d{1,4}\.\d{1,4}\.\d{1,6}$")]
    private static partial Regex VersionPattern();
}
