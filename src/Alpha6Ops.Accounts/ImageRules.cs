using System.Buffers.Binary;
using System.Security.Cryptography;
using Alpha6Ops.Identity;

namespace Alpha6Ops.Accounts;

// Validates uploaded images by content, never by file name: PNG, JPEG and WebP only, bounded size and pixels.
// No decoder is involved, so a malformed file can only be rejected, never executed or rendered server-side.
public static class ImageRules
{
    public const int MaxBytes = 1024 * 1024;
    public const int MaxDimension = 2048;
    public const int MinDimension = 32;

    public sealed record ValidatedImage(string ContentType, int Width, int Height, string Sha256);

    public static ValidatedImage Validate(ReadOnlySpan<byte> bytes)
    {
        static IdentityException Invalid(string message) => new("invalid_image", message, 400);
        if (bytes.Length == 0) throw Invalid("Choose an image file.");
        if (bytes.Length > MaxBytes) throw Invalid("Images must be 1 MB or smaller.");
        string type; int width, height;
        if (bytes.Length > 24 && bytes[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) && bytes[12..16].SequenceEqual("IHDR"u8))
        {
            type = "image/png";
            width = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes[16..20]);
            height = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes[20..24]);
        }
        else if (bytes.Length > 4 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            type = "image/jpeg";
            (width, height) = JpegSize(bytes) ?? throw Invalid("This JPEG could not be read.");
        }
        else if (bytes.Length > 30 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            type = "image/webp";
            (width, height) = WebpSize(bytes) ?? throw Invalid("This WebP could not be read.");
        }
        else throw Invalid("Use a PNG, JPEG or WebP image.");
        if (width < MinDimension || height < MinDimension) throw Invalid($"Images must be at least {MinDimension}×{MinDimension} pixels.");
        if (width > MaxDimension || height > MaxDimension) throw Invalid($"Images must be {MaxDimension}×{MaxDimension} pixels or smaller.");
        return new(type, width, height, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private static (int, int)? JpegSize(ReadOnlySpan<byte> b)
    {
        var i = 2;
        while (i + 9 < b.Length)
        {
            if (b[i] != 0xFF) return null;
            var marker = b[i + 1];
            if (marker == 0xD8 || (marker >= 0xD0 && marker <= 0xD7) || marker == 0x01 || marker == 0xFF) { i += marker == 0xFF ? 1 : 2; continue; }
            var length = BinaryPrimitives.ReadUInt16BigEndian(b[(i + 2)..(i + 4)]);
            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
                return (BinaryPrimitives.ReadUInt16BigEndian(b[(i + 7)..(i + 9)]), BinaryPrimitives.ReadUInt16BigEndian(b[(i + 5)..(i + 7)]));
            if (marker == 0xDA) return null;
            i += 2 + length;
        }
        return null;
    }

    private static (int, int)? WebpSize(ReadOnlySpan<byte> b)
    {
        var chunk = b[12..16];
        if (chunk.SequenceEqual("VP8X"u8) && b.Length >= 30)
            return (1 + (b[24] | b[25] << 8 | b[26] << 16), 1 + (b[27] | b[28] << 8 | b[29] << 16));
        if (chunk.SequenceEqual("VP8L"u8) && b.Length >= 25)
        {
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(b[21..25]);
            return (1 + (int)(bits & 0x3FFF), 1 + (int)((bits >> 14) & 0x3FFF));
        }
        if (chunk.SequenceEqual("VP8 "u8) && b.Length >= 30)
            return (BinaryPrimitives.ReadUInt16LittleEndian(b[26..28]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(b[28..30]) & 0x3FFF);
        return null;
    }
}
