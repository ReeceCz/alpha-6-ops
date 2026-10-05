using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Alpha6Ops.Identity;

namespace Alpha6Ops.Accounts;

public enum FeedbackCategory { FlightRecording = 0, Bug = 1, Idea = 2, Other = 3 }
public enum FeedbackStatus { New = 0, Handled = 1 }
public sealed record FeedbackRequest(FeedbackCategory Category, string? Message, string? FlightNumber, DateOnly? FlownOn);
public sealed record FeedbackFile(string FileName, byte[] Bytes);
public sealed record CleanAttachment(string FileName, string ContentType, byte[] Bytes, string Sha256);

/// <summary>Validation for problem reports. Attachment types are decided by content, never by the file name.</summary>
public static partial class FeedbackRules
{
    public const int MaxAttachmentBytes = 5_242_880;
    public const int PerHour = 5;
    public const long StorageCapBytes = 200L * 1024 * 1024;

    public static FeedbackRequest Request(FeedbackRequest request, DateOnly today)
    {
        static IdentityException Invalid(string message) => new("invalid_feedback", message, 400);
        if (!Enum.IsDefined(request.Category)) throw Invalid("Choose what kind of problem this is.");
        var message = (request.Message ?? "").Trim();
        if (message.Length is < 10 or > 4000) throw Invalid("Describe the problem in 10 to 4000 characters.");
        var flight = (request.FlightNumber ?? "").Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "");
        if (flight.Length > 0 && !FlightNumberPattern().IsMatch(flight)) throw Invalid("Flight number must be 2 to 10 letters or digits, or blank.");
        if (request.FlownOn is { } date && (date > today || date < new DateOnly(2020, 1, 1))) throw Invalid("The flight date must be between 2020 and today.");
        return new(request.Category, message, flight, request.FlownOn);
    }

    public static CleanAttachment Attachment(FeedbackFile file)
    {
        static IdentityException Invalid(string message) => new("invalid_attachment", message, 400);
        var bytes = file.Bytes ?? [];
        if (bytes.Length is 0 or > MaxAttachmentBytes) throw Invalid("Attach one file of up to 5 MB.");
        var (type, extension) = Detect(bytes) ?? throw Invalid("Attach a PNG or JPEG screenshot, or the desktop's exported test log (.zip or .json).");
        var stem = Path.GetFileNameWithoutExtension(file.FileName ?? "");
        stem = UnsafeName().Replace(stem, "_").Trim('.');
        if (stem.Length == 0) stem = "attachment";
        if (stem.Length > 100) stem = stem[..100];
        return new(stem + extension, type, bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private static (string Type, string Extension)? Detect(byte[] b)
    {
        if (b.Length >= 8 && b.AsSpan(0, 8).SequenceEqual((byte[])[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return ("image/png", ".png");
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ("image/jpeg", ".jpg");
        if (b.Length >= 4 && b[0] == 0x50 && b[1] == 0x4B && b[2] == 0x03 && b[3] == 0x04) return ("application/zip", ".zip");
        try
        {
            var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(b).TrimStart('﻿');
            using var _ = JsonDocument.Parse(text);
            return ("application/json", ".json");
        }
        catch (Exception ex) when (ex is DecoderFallbackException or JsonException) { return null; }
    }

    [GeneratedRegex("^[A-Z0-9]{2,10}$")]
    private static partial Regex FlightNumberPattern();
    [GeneratedRegex("[^A-Za-z0-9._-]")]
    private static partial Regex UnsafeName();
}
