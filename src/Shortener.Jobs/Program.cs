using Shortener.ServiceDefaults;
using Shortener.Jobs;
var builder = WebApplication.CreateBuilder(args);
builder.AddFoundation("shortener-jobs");
var app = builder.Build();
await JobCommands.RunAsync(app, args);
public partial class Program;
