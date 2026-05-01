using BackupArchiver;
using Xunit;

namespace BackupArchiver.Tests;

public class ArchiverServiceTests : IDisposable
{
    private readonly string _testRoot;

    public ArchiverServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"coldmigrator_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testRoot, true); } catch { }
    }

    private static string DaysAgo(int days) => DateTime.Now.AddDays(-days).ToString("yyyyMMdd");

    // ===== Core: Old vs Recent filtering =====

    [Fact]
    public void Scan_FindsOldDirs_ExcludesRecent()
    {
        var old = Path.Combine(_testRoot, DaysAgo(10));
        var recent = Path.Combine(_testRoot, DaysAgo(2));
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(recent);

        var opt = new Options
        {
            Source = _testRoot,
            Days = 5,
            SourcePattern = "yyyyMMdd",
            Remote = "smb:/dest",
            Config = "/dev/null"
        };
        var svc = new ArchiverService(opt);

        var dirs = svc.ScanDateFolders(_testRoot, 5);

        Assert.Single(dirs);
        Assert.Equal(old, dirs[0]);
    }

    [Fact]
    public void Scan_IgnoresNonDateDirs()
    {
        Directory.CreateDirectory(Path.Combine(_testRoot, DaysAgo(10)));
        Directory.CreateDirectory(Path.Combine(_testRoot, "temp"));
        Directory.CreateDirectory(Path.Combine(_testRoot, "backup"));

        var opt = new Options
        {
            Source = _testRoot,
            Days = 5,
            SourcePattern = "yyyyMMdd",
            Remote = "smb:/dest",
            Config = "/dev/null"
        };
        var svc = new ArchiverService(opt);

        var dirs = svc.ScanDateFolders(_testRoot, 5);

        Assert.Single(dirs);
    }

    // ===== Exclude =====

    [Fact]
    public void Exclude_ExactName_RemovesMatched()
    {
        var old = DaysAgo(10);
        var keep = DaysAgo(9);
        Directory.CreateDirectory(Path.Combine(_testRoot, old));
        Directory.CreateDirectory(Path.Combine(_testRoot, keep));

        var opt = new Options
        {
            Source = _testRoot,
            Days = 5,
            SourcePattern = "yyyyMMdd",
            Remote = "smb:/dest",
            Config = "/dev/null",
            ExcludePatterns = new[] { old }
        };
        var svc = new ArchiverService(opt);

        var dirs = svc.ScanDateFolders(_testRoot, 5);

        Assert.Single(dirs);
        Assert.Equal(keep, Path.GetFileName(dirs[0]));
    }

    // Note: wildcard * matches any characters; pattern must still produce a valid 8-digit date.
    // "yyyyMMdd'_'____" would match "20240501_backup" (4 underscore-suffix chars).

    // ===== Dest Path =====

    [Fact]
    public void BuildDestPath_ReplacesDateToken()
    {
        var old = DaysAgo(10);
        Directory.CreateDirectory(Path.Combine(_testRoot, old));

        var opt = new Options
        {
            Source = _testRoot,
            Days = 5,
            SourcePattern = "yyyyMMdd",
            DestPattern = "archive/{date}/backup",
            Remote = "smb:/dest",
            Config = "/dev/null"
        };
        var svc = new ArchiverService(opt);

        var result = svc.BuildDestPath(Path.Combine(_testRoot, old));

        Assert.Equal($"archive/{old}/backup", result);
    }

    [Fact]
    public void BuildDestPath_ReplacesIndividualComponents()
    {
        var d = DateTime.Now.AddDays(-10);
        var yyyy = d.ToString("yyyy");
        var mm = d.ToString("MM");
        var dd = d.ToString("dd");
        Directory.CreateDirectory(Path.Combine(_testRoot, yyyy, mm, dd));

        var opt = new Options
        {
            Source = _testRoot,
            Days = 5,
            SourcePattern = "yyyy/MM/dd",
            DestPattern = "backup/yyyy/MM/dd",
            Remote = "smb:/dest",
            Config = "/dev/null"
        };
        var svc = new ArchiverService(opt);

        var result = svc.BuildDestPath(Path.Combine(_testRoot, yyyy, mm, dd));

        Assert.Equal($"backup/{yyyy}/{mm}/{dd}", result);
    }
}
