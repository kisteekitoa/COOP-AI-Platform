using COOPAI.API.Models;
using COOPAI.API.Models.Import;
using Microsoft.EntityFrameworkCore;

namespace COOPAI.API.Data;

public class CoopDbContext : DbContext
{
    public CoopDbContext(DbContextOptions<CoopDbContext> options)
        : base(options)
    {
    }

    #region Master Data

    public DbSet<Member> Members => Set<Member>();

    public DbSet<LoanType> LoanTypes => Set<LoanType>();

    #endregion

    #region Loan

    public DbSet<LoanContract> LoanContracts => Set<LoanContract>();

    public DbSet<LoanPayment> LoanPayments => Set<LoanPayment>();

    public DbSet<Guarantor> Guarantors => Set<Guarantor>();

    #endregion

    #region Import

    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    public DbSet<ImportLog> ImportLogs => Set<ImportLog>();

    public DbSet<ImportLoanRecord> ImportLoanRecords => Set<ImportLoanRecord>();

    #endregion

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureMember(modelBuilder);

        ConfigureLoanContract(modelBuilder);

        ConfigureLoanPayment(modelBuilder);

        ConfigureGuarantor(modelBuilder);

        ConfigureLoanType(modelBuilder);

        ConfigureImport(modelBuilder);

        ConfigureImportLoanRecord(modelBuilder);
    }

    #region Entity Configuration

    private static void ConfigureMember(ModelBuilder builder)
    {
        builder.Entity<Member>()
            .HasIndex(x => x.MemberNo);

        builder.Entity<Member>()
            .Property(x => x.MemberNo)
            .HasMaxLength(20);

        builder.Entity<Member>()
            .Property(x => x.FullName)
            .HasMaxLength(200);
    }

    private static void ConfigureLoanContract(ModelBuilder builder)
    {
        builder.Entity<LoanContract>()
            .HasIndex(x => x.ContractNo);

        builder.Entity<LoanContract>()
            .Property(x => x.ContractNo)
            .HasMaxLength(30);
    }

    private static void ConfigureLoanPayment(ModelBuilder builder)
    {
    }

    private static void ConfigureGuarantor(ModelBuilder builder)
    {
    }

    private static void ConfigureLoanType(ModelBuilder builder)
    {
    }

    private static void ConfigureImport(ModelBuilder builder)
    {
    }

    private static void ConfigureImportLoanRecord(ModelBuilder builder)
    {
        builder.Entity<ImportLoanRecord>()
            .HasIndex(x => x.BatchId);

        builder.Entity<ImportLoanRecord>()
            .HasIndex(x => x.MemberNo);

        builder.Entity<ImportLoanRecord>()
            .HasIndex(x => x.ContractNo);
    }

    #endregion
}