using CommandLine;

namespace BackupArchiver;

public class Options
{
    // === 基本參數 ===
    [Option("source", Required = true, HelpText = "來源根目錄")]
    public string Source { get; set; } = string.Empty;

    [Option("days", Required = true, HelpText = "超過幾天要搬家")]
    public int Days { get; set; }

    [Option("source-pattern", Default = "yyyyMMdd", HelpText = "來源資料夾的日期階層，例如 yyyy/MM/dd")]
    public string SourcePattern { get; set; } = "yyyyMMdd";

    [Option("remote", Required = true, HelpText = "rclone remote 名稱 + 目的地路徑，例如 smb-daily:/archive/")]
    public string Remote { get; set; } = string.Empty;

    [Option("dest-pattern", Default = "{date}", HelpText = "目的地資料夾格式，例如 {date} 或 yyyy/MM/dd")]
    public string DestPattern { get; set; } = "{date}";

    [Option("config", Required = true, HelpText = "rclone config 檔路徑")]
    public string Config { get; set; } = string.Empty;

    [Option("compress", Default = true, HelpText = "是否壓縮成 zip")]
    public bool Compress { get; set; } = true;

    [Option("log", Default = "backup.log", HelpText = "文字 Log 檔路徑")]
    public string Log { get; set; } = "backup.log";

    [Option("dry-run", Default = false, HelpText = "預覽模式：顯示哪些資料夾會被處理，但不改變任何檔案")]
    public bool DryRun { get; set; }

    [Option("exclude-pattern", HelpText = "排除的資料夾名稱（可多次指定），支援 * 万用字元")]
    public IEnumerable<string> ExcludePatterns { get; set; } = Array.Empty<string>();

    // === Hangfire 參數 ===
    [Option("hangfire", Default = false, HelpText = "是否啟用 Hangfire 排程模式")]
    public bool Hangfire { get; set; }

    [Option("hf-storage", HelpText = "Hangfire 儲存資料庫類型：pg 或 mssql")]
    public string? HfStorage { get; set; }

    [Option("hf-interval", Default = "0 1 * * *", HelpText = "Cron 表達式，執行頻率")]
    public string HfInterval { get; set; } = "0 1 * * *";

    [Option("hf-dashboard", Default = false, HelpText = "是否開啟 Hangfire Dashboard")]
    public bool HfDashboard { get; set; }

    [Option("hf-port", Default = 5000, HelpText = "Dashboard port")]
    public int HfPort { get; set; } = 5000;
}
