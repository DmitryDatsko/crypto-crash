using CryptoCrush.Data;
using Microsoft.EntityFrameworkCore;

namespace CryptoCrush.DbInitializer;

public class DbInitializer(IDbContextFactory<ApiDbContext> context) : IDbInitializer
{
    public void Initialize()
    {
        using var db = context.CreateDbContext();
        db.Database.Migrate();
    }
}