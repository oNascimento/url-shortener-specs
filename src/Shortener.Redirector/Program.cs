using Shortener.ServiceDefaults;
using Shortener.Redirector;

var builder = WebApplication.CreateBuilder(args);
builder.AddFoundation("shortener-redirector");
builder.Services.AddScoped<IRedirectResolver, RedirectResolver>();
builder.Services.AddSingleton<AccessCapture>();
builder.ConfigureRedirectForwarding();
var app = builder.Build();

// Public requests finish here, before the management authentication pipeline.
app.UseRedirectForwarding();
app.UseMiddleware<RedirectMiddleware>();
app.UseFoundation();
app.Run();
public partial class Program;
