namespace VvvvMcp.Core.Services;

public static class VlIdGenerator
{
    private static readonly Random _rng = new();
    private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public static string NewId()
    {
        // Generate 22-character alphanumeric ID matching vvvv's format
        return string.Create(22, _rng, static (span, rng) =>
        {
            for (int i = 0; i < span.Length; i++)
                span[i] = Chars[rng.Next(Chars.Length)];
        });
    }
}
