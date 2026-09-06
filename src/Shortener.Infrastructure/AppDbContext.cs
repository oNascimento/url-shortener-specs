using Microsoft.EntityFrameworkCore;
namespace Shortener.Infrastructure;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);
