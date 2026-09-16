using System.Security.Cryptography;
using COOPAI.API.Services.DebtSegmentation;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OfficeOpenXml;

namespace COOPAI.API.Tests;

public sealed class InstallmentMasterSyncServiceTests
{
    [Fact]
    public async Task ValidWorkbookPublishes_AndSameShaIsNoChange()
    {
        var path = InstallmentMasterWorkbookReaderTests.CreateWorkbook([("C1", 12m, 100m)]);
        try
        {
            var store = new InMemoryInstallmentMasterSnapshotStore();
            var service = CreateService(path, store);

            var first = await service.SyncNowAsync();
            var repeated = await service.SyncAutoAsync();
            var history = await service.GetHistoryAsync(10);

            Assert.Equal(DebtSyncStatuses.Success, first.Status);
            Assert.True(first.Published);
            Assert.StartsWith($"{MonthlyAmountDueVersion.InstallmentMasterV3}-", first.SnapshotId, StringComparison.Ordinal);
            Assert.Equal(InstallmentMasterSchemaModes.Headered, first.SchemaMode);
            Assert.Equal(DebtSyncStatuses.NoChange, repeated.Status);
            Assert.False(repeated.Published);
            Assert.Equal(first.SnapshotId, repeated.SnapshotId);
            Assert.Equal(["Auto", "Manual"], history.Select(x => x.Trigger).ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ChangedValidShaCreatesNewCurrentSnapshot()
    {
        var path = InstallmentMasterWorkbookReaderTests.CreateWorkbook([("C1", 12m, 100m)]);
        try
        {
            var store = new InMemoryInstallmentMasterSnapshotStore();
            var service = CreateService(path, store);
            var first = await service.SyncNowAsync();
            using (var package = new ExcelPackage(new FileInfo(path)))
            {
                package.Workbook.Worksheets["2534-2569"].Cells[2, 39].Value = 200m;
                package.Save();
            }

            var changed = await service.SyncAutoAsync();

            Assert.Equal(DebtSyncStatuses.Success, changed.Status);
            Assert.NotEqual(first.SnapshotId, changed.SnapshotId);
            Assert.Equal(200m, (await store.GetCurrentAsync())?.Workbook.Contracts.Single().MonthlyInstallment);
            Assert.Equal(1_200m, (await store.GetCurrentAsync())?.Workbook.Contracts.Single().ContractualObligation);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InvalidCandidateRetainsLastValidSnapshot()
    {
        var path = InstallmentMasterWorkbookReaderTests.CreateWorkbook([("C1", 12m, 100m)]);
        try
        {
            var store = new InMemoryInstallmentMasterSnapshotStore();
            var service = CreateService(path, store);
            var first = await service.SyncNowAsync();
            using (var package = new ExcelPackage(new FileInfo(path)))
            {
                package.Workbook.Worksheets["2534-2569"].Cells[1, 38].Value = "UNSAFE";
                package.Save();
            }

            var failed = await service.SyncAutoAsync();

            Assert.Equal(DebtSyncStatuses.Failed, failed.Status);
            Assert.True(failed.PreviousSnapshotPreserved);
            Assert.Equal(InstallmentMasterSchemaModes.Headered, failed.SchemaMode);
            Assert.Equal(first.SnapshotId, (await store.GetCurrentAsync())?.SnapshotId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InvalidHeaderlessCandidateRetainsLastValidSnapshot()
    {
        var path = InstallmentMasterWorkbookReaderTests.CreateHeaderlessWorkbook();
        try
        {
            var store = new InMemoryInstallmentMasterSnapshotStore();
            var service = CreateService(path, store, worksheetName: "รายละเอียดลูกหนี้ 34-69");
            var first = await service.SyncNowAsync();
            using (var package = new ExcelPackage(new FileInfo(path)))
            {
                var sheet = package.Workbook.Worksheets["รายละเอียดลูกหนี้ 34-69"];
                for (var row = 1; row <= 25; row++)
                    sheet.Cells[row, 38].Value = "INVALID";
                package.Save();
            }

            var failed = await service.SyncAutoAsync();

            Assert.Equal(DebtSyncStatuses.Success, first.Status);
            Assert.Equal(InstallmentMasterSchemaModes.HeaderlessPositional, first.SchemaMode);
            Assert.Equal(DebtSyncStatuses.Failed, failed.Status);
            Assert.True(failed.PreviousSnapshotPreserved);
            Assert.Equal(InstallmentMasterSchemaModes.HeaderlessPositional, failed.SchemaMode);
            Assert.Equal(first.SnapshotId, (await store.GetCurrentAsync())?.SnapshotId);
            Assert.Contains(failed.Errors,
                error => error.Contains("HEADERLESS_POSITIONAL", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RowLevelInvalidValuesDoNotInvalidateWholeWorkbook()
    {
        var path = InstallmentMasterWorkbookReaderTests.CreateWorkbook([
            ("C1", 12m, 100m), ("C2", null, 100m), ("C3", 12m, -1m)
        ]);
        try
        {
            var result = await CreateService(path, new InMemoryInstallmentMasterSnapshotStore()).SyncNowAsync();

            Assert.Equal(DebtSyncStatuses.Success, result.Status);
            Assert.Equal(1, result.ValidContractCount);
            Assert.Equal(2, result.InvalidContractCount);
            Assert.Equal(1, result.InvalidTotalInstallmentRows);
            Assert.Equal(1, result.InvalidMonthlyInstallmentRows);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SharedPreviewGatePreventsOverlappingSourceImports()
    {
        var path = InstallmentMasterWorkbookReaderTests.CreateWorkbook([("C1", 12m, 100m)]);
        var gate = new PreviewSyncGate();
        try
        {
            var service = CreateService(path, new InMemoryInstallmentMasterSnapshotStore(), gate);
            await gate.Semaphore.WaitAsync();
            try
            {
                var busy = await service.SyncAutoAsync();
                Assert.Equal(DebtSyncStatuses.Busy, busy.Status);
            }
            finally
            {
                gate.Semaphore.Release();
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SourceWorkbookRemainsByteAndMetadataIdentical()
    {
        var path = InstallmentMasterWorkbookReaderTests.CreateWorkbook([("C1", 12m, 100m)]);
        try
        {
            var beforeHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
            var beforeWrite = File.GetLastWriteTimeUtc(path);
            var beforeLength = new FileInfo(path).Length;

            await CreateService(path, new InMemoryInstallmentMasterSnapshotStore()).SyncNowAsync();

            var afterHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
            Assert.Equal(beforeHash, afterHash);
            Assert.Equal(beforeWrite, File.GetLastWriteTimeUtc(path));
            Assert.Equal(beforeLength, new FileInfo(path).Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static InstallmentMasterSyncService CreateService(
        string path,
        IInstallmentMasterSnapshotStore store,
        PreviewSyncGate? gate = null,
        string worksheetName = "2534-2569")
    {
        var options = Options.Create(new InstallmentMasterOptions
        {
            Enabled = true,
            WorkbookPath = path,
            WorksheetName = worksheetName,
            StabilizationDelayMilliseconds = 0,
            NetworkStabilizationDelayMilliseconds = 0,
            MinimumFileAgeSeconds = 0
        });
        var environment = new TestEnvironment(Path.GetDirectoryName(path)!);
        return new InstallmentMasterSyncService(
            options,
            new InstallmentMasterWorkbookAcquirer(options, environment),
            new InstallmentMasterWorkbookReader(),
            store,
            gate ?? new PreviewSyncGate());
    }

    private sealed class TestEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "COOPAI.API.Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
