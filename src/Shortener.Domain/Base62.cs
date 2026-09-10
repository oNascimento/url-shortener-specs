namespace Shortener.Domain;

public static class Base62
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string Encode(long id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        Span<char> buffer = stackalloc char[11];
        var position = buffer.Length;
        do
        {
            buffer[--position] = Alphabet[(int)(id % 62)];
            id /= 62;
        } while (id != 0);
        return new string(buffer[position..]);
    }
}
