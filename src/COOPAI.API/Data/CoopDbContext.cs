using COOPAI.API.Models;
using COOPAI.API.Models.Import;
using COOPAI.API.Models.Portfolio;
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

    #region Portfolio Snapshot

    public DbSet<PortfolioSnapshot> PortfolioSnapshots => Set<PortfolioSnapshot>();

    public DbSet<PortfolioSnapshotRecord> PortfolioSnapshotRecords => Set<PortfolioSnapshotRecord>();

    public DbSet<PortfolioSnapshotExclusion> PortfolioSnapshotExclusions => Set<PortfolioSnapshotExclusion>();

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

        ConfigurePortfolioSnapshot(modelBuilder);
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

    private static void ConfigurePortfolioSnapshot(ModelBuilder builder)
    {
        var snapshot = builder.Entity<PortfolioSnapshot>();
        snapshot.HasKey(x => x.Id);
        snapshot.HasIndex(x => new { x.AsOfDate, x.Revision }).IsUnique();
        snapshot.HasIndex(x => new { x.Status, x.AsOfDate });
        snapshot.HasIndex(x => new { x.SourceType, x.SourceFileHash });
        snapshot.HasIndex(x => new
        {
            x.SourceFileHash,
            x.AsOfDate,
            x.DefinitionVersion,
            x.SnapshotContentHash
        }).IsUnique();
        snapshot.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        snapshot.Property(x => x.CurrencyCode).HasMaxLength(3);
        snapshot.Property(x => x.SourceType).HasMaxLength(32);
        snapshot.Property(x => x.SourceFileName).HasMaxLength(260);
        snapshot.Property(x => x.SourceFileHash).HasMaxLength(64);
        snapshot.Property(x => x.SnapshotContentHash).HasMaxLength(64);
        snapshot.Property(x => x.RejectionReason).HasMaxLength(1000);
        ConfigureSnapshotMoney(snapshot);

        var record = builder.Entity<PortfolioSnapshotRecord>();
        record.HasKey(x => x.Id);
        record.HasIndex(x => new { x.PortfolioSnapshotId, x.NormalizedContractNo }).IsUnique();
        record.HasIndex(x => new { x.PortfolioSnapshotId, x.LoanTypePrefix });
        record.HasIndex(x => new { x.PortfolioSnapshotId, x.MemberId });
        record.HasIndex(x => new { x.PortfolioSnapshotId, x.TermStatus, x.BalanceStatus });
        record.HasIndex(x => new { x.LoanContractId, x.PortfolioSnapshotId });
        record.Property(x => x.SourceRecordKey).HasMaxLength(64);
        record.Property(x => x.NormalizedContractNo).HasMaxLength(30);
        record.Property(x => x.LoanTypePrefix).HasMaxLength(8);
        record.Property(x => x.SourceRowKind).HasConversion<string>().HasMaxLength(24);
        record.Property(x => x.OpeningSide).HasConversion<string>().HasMaxLength(16);
        record.Property(x => x.TermStatus).HasConversion<string>().HasMaxLength(16);
        record.Property(x => x.BalanceStatus).HasConversion<string>().HasMaxLength(16);
        record.Property(x => x.CanonicalMatchStatus).HasConversion<string>().HasMaxLength(16);
        record.Property(x => x.MemberMatchStatus).HasConversion<string>().HasMaxLength(16);
        record.Property(x => x.InclusionStatus).HasConversion<string>().HasMaxLength(32);
        record.Property(x => x.WarningCodesJson).HasMaxLength(1000);
        ConfigureSnapshotRecordMoney(record);
        record.HasOne(x => x.PortfolioSnapshot)
            .WithMany(x => x.Records)
            .HasForeignKey(x => x.PortfolioSnapshotId)
            .OnDelete(DeleteBehavior.NoAction);
        record.HasOne(x => x.LoanContract)
            .WithMany()
            .HasForeignKey(x => x.LoanContractId)
            .OnDelete(DeleteBehavior.NoAction);
        record.HasOne(x => x.Member)
            .WithMany()
            .HasForeignKey(x => x.MemberId)
            .OnDelete(DeleteBehavior.NoAction);

        var exclusion = builder.Entity<PortfolioSnapshotExclusion>();
        exclusion.HasKey(x => x.Id);
        exclusion.HasIndex(x => new { x.PortfolioSnapshotId, x.LoanContractId, x.ReasonCode }).IsUnique();
        exclusion.Property(x => x.ReasonCode).HasMaxLength(64);
        exclusion.HasOne(x => x.PortfolioSnapshot)
            .WithMany(x => x.Exclusions)
            .HasForeignKey(x => x.PortfolioSnapshotId)
            .OnDelete(DeleteBehavior.NoAction);
        exclusion.HasOne(x => x.LoanContract)
            .WithMany()
            .HasForeignKey(x => x.LoanContractId)
            .OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureSnapshotMoney(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<PortfolioSnapshot> entity)
    {
        entity.Property(x => x.PrincipalOpening).HasPrecision(19, 2);
        entity.Property(x => x.ProfitOpening).HasPrecision(19, 2);
        entity.Property(x => x.TotalOpening).HasPrecision(19, 2);
        entity.Property(x => x.PrincipalRepayment).HasPrecision(19, 2);
        entity.Property(x => x.ProfitRepayment).HasPrecision(19, 2);
        entity.Property(x => x.TotalRepayment).HasPrecision(19, 2);
        entity.Property(x => x.PrincipalOutstanding).HasPrecision(19, 2);
        entity.Property(x => x.ProfitOutstanding).HasPrecision(19, 2);
        entity.Property(x => x.TotalOutstanding).HasPrecision(19, 2);
        entity.Property(x => x.PrincipalDifference).HasPrecision(19, 2);
        entity.Property(x => x.ProfitDifference).HasPrecision(19, 2);
        entity.Property(x => x.TotalDifference).HasPrecision(19, 2);
        entity.Property(x => x.ComponentDifference).HasPrecision(19, 2);
        entity.Property(x => x.ExpiredOutstandingTotal).HasPrecision(19, 2);
    }

    private static void ConfigureSnapshotRecordMoney(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<PortfolioSnapshotRecord> entity)
    {
        entity.Property(x => x.PrincipalOpening).HasPrecision(19, 2);
        entity.Property(x => x.ProfitOpening).HasPrecision(19, 2);
        entity.Property(x => x.TotalOpening).HasPrecision(19, 2);
        entity.Property(x => x.PrincipalRepayment).HasPrecision(19, 2);
        entity.Property(x => x.ProfitRepayment).HasPrecision(19, 2);
        entity.Property(x => x.TotalRepayment).HasPrecision(19, 2);
        entity.Property(x => x.PrincipalOutstanding).HasPrecision(19, 2);
        entity.Property(x => x.ProfitOutstanding).HasPrecision(19, 2);
        entity.Property(x => x.TotalOutstanding).HasPrecision(19, 2);
    }

    #endregion
}
