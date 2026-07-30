using COOPAI.API.Models;
using Microsoft.EntityFrameworkCore;

namespace COOPAI.API.Data;

public class CoopDbContext : DbContext
{
    public CoopDbContext(DbContextOptions<CoopDbContext> options)
        : base(options)
    {
    }

    public DbSet<Member> Members => Set<Member>();

    public DbSet<LoanContract> LoanContracts => Set<LoanContract>();

    public DbSet<LoanType> LoanTypes => Set<LoanType>();

    public DbSet<Guarantor> Guarantors => Set<Guarantor>();

    public DbSet<LoanPayment> LoanPayments => Set<LoanPayment>();

    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    public DbSet<ImportLog> ImportLogs => Set<ImportLog>();
}