namespace Shortener.Application;

public sealed record CreateLinkInput(string DestinationUrl);
public sealed record LinkResult(long Id, string Code, string ShortUrl, string DestinationUrl, DateTimeOffset CreatedAt, string Status);
public sealed record LinkPage(IReadOnlyList<LinkResult> Items, string? NextCursor);

public sealed class LinkFailure(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public interface IWriterIsolation
{
    Task<bool> IsConfirmedAsync(CancellationToken cancellationToken);
}
