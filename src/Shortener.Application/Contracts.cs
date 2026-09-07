namespace Shortener.Application;
// Adapters are implemented with the features that consume them. Cancellation is mandatory.
public interface IEmailSender
{
    Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken);
}
public sealed record RegisterInput(string Email, string Password);
public sealed record EmailInput(string Email);
public sealed record LoginInput(string Email, string Password);
public sealed record ActionTokenInput(string Token);
public sealed record ResetPasswordInput(string Token, string NewPassword);
public sealed record AuthResult(string AccessToken, string TokenType, int ExpiresIn, UserResult User);
public sealed record UserResult(Guid Id, string Email, bool EmailVerified, string Role, DateTimeOffset CreatedAt);
public sealed record AccessRecorded(int SchemaVersion, Guid EventId, string LinkId, DateTimeOffset OccurredAt, string SourceIp);
public enum PublishOutcome { Confirmed, Rejected, Unknown }
public interface IAccessPublisher
{
    Task<PublishOutcome> PublishAsync(AccessRecorded message, CancellationToken cancellationToken);
}
public sealed record DeletionMarker(Guid UserId, Guid JobId, DateTimeOffset RequestedAt);
public interface IRecoveryRegistry
{
    Task<long> ReadUpperBoundAsync(CancellationToken cancellationToken);
    Task<bool> CompareExchangeUpperBoundAsync(long expected, long next, CancellationToken cancellationToken);
    Task<DeletionMarker> RecordDeletionAsync(DeletionMarker marker, CancellationToken cancellationToken);
}
