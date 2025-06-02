using CryptoCrush.Data;
using Microsoft.EntityFrameworkCore;

namespace CryptoCrush.DbInitializer;

public class DbInitializer(ApiDbContext context) : IDbInitializer
{
    public void Initialize()
    {
        context.Database.Migrate();
    }
}