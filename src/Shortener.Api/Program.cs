using Shortener.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddFoundation("shortener-api");
var app = builder.Build();

app.UseFoundation();
app.Run();
public partial class Program;
