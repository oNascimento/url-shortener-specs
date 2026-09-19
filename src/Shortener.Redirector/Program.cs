using Shortener.ServiceDefaults;
using Shortener.Redirector;
using Shortener.Application;

var builder = WebApplication.CreateBuilder(args);
builder.AddFoundation("shortener-redirector");
builder.Services.AddScoped<IRedirectResolver, RedirectResolver>();
builder.Services.AddRedirectReadiness();
builder.Services.AddSingleton<AccessCapture>();
builder.ConfigureRedirectForwarding();
builder.Services.AddSingleton(RabbitSettings.From(builder.Configuration));
builder.Services.AddSingleton<RabbitTransport>();
builder.Services.AddSingleton<IConfirmTransport>(services => services.GetRequiredService<RabbitTransport>());
builder.Services.AddHostedService(services => services.GetRequiredService<RabbitTransport>());
builder.Services.AddSingleton<IAccessPublisher, BoundedAccessPublisher>();
var app = builder.Build();

// Public requests finish here, before the management authentication pipeline.
app.UseRedirectForwarding();
app.UseMiddleware<RedirectMiddleware>();
app.UseFoundation();
app.Run();
public partial class Program;
