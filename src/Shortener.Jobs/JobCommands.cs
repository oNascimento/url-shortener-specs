using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shortener.Application;
using Shortener.Infrastructure;
using Shortener.ServiceDefaults;

namespace Shortener.Jobs;

public static class JobCommands
{
    public static async Task RunAsync(WebApplication app, string[] args)
    {
        if (args.Contains("--migrate"))
        {
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        }
        else if (args.Contains("--suspend-link-creation") || args.Contains("--prepare-link-restore"))
        {
            var sequence = new LinkSequence(app.Services.GetRequiredService<IRecoveryRegistry>());
            await using var connection = new NpgsqlConnection(app.Configuration.GetConnectionString("Primary"));
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            if (args.Contains("--suspend-link-creation")) await sequence.SuspendAsync(connection, default);
            else
            {
                var oldPrimary = app.Configuration.GetConnectionString("OldPrimary") ?? throw new InvalidOperationException("ConnectionStrings:OldPrimary is required for restore verification.");
                var role = app.Configuration["Recovery:WriterRole"] ?? throw new InvalidOperationException("Recovery:WriterRole is required for restore verification.");
                await sequence.PrepareRestoreAsync(connection, new PostgresWriterIsolation(oldPrimary, role), default);
            }
            await transaction.CommitAsync();
        }
        else
        {
            app.UseFoundation();
            await app.RunAsync();
        }
    }
}
