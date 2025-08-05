using Microsoft.EntityFrameworkCore;

namespace SWA
{
    public class ApContext : DbContext
    {
        public DbSet<ValueEntry> ValueEntries { get; set; }
        public DbSet<ResultEntry> ResultEntries { get; set; }
        public DbSet<CsvModel> CsvModel { get; set; }

        public ApContext(DbContextOptions<ApContext> options)
            : base(options)
        {
            Database.EnsureCreated();
        }
    }
}
