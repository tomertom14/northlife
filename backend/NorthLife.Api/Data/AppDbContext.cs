using Microsoft.EntityFrameworkCore;

namespace NorthLife.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);
