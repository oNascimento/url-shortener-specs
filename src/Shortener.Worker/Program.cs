using Shortener.ServiceDefaults;
using Shortener.Infrastructure;
using Shortener.Worker;

var builder = WebApplication.CreateBuilder(args);
builder.AddFoundation("shortener-worker");
builder.Services.AddSingleton(sp => new AccessBatchStore(builder.Configuration.GetConnectionString("Primary")!, sp.GetRequiredService<TimeProvider>()));
builder.Services.AddHostedService<AccessConsumer>();
var app = builder.Build();

app.UseFoundation();
app.Run();
public partial class Program;
