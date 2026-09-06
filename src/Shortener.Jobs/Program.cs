using Shortener.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Shortener.Infrastructure;
var builder = WebApplication.CreateBuilder(args);
builder.AddFoundation("shortener-jobs");
var app = builder.Build();
if (args.Contains("--migrate")) { using var scope = app.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(); return; }
app.UseFoundation();
app.Run();
public partial class Program;
