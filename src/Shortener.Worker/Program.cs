using Shortener.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddFoundation("shortener-worker");
var app = builder.Build();

app.UseFoundation();
app.Run();
public partial class Program;
