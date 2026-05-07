using BoardGames.Models;
using Microsoft.EntityFrameworkCore;

namespace BoardGames.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }
}