using System.Runtime.CompilerServices;
using BackupArchiver;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using CommandLine;

[assembly: InternalsVisibleTo("BackupArchiver.Tests")]

// === 解析參數 ===
var result = Parser.Default.ParseArguments<Options>(args);

if (result.Tag == ParserResultType.NotParsed)
{
    Console.WriteLine("參數錯誤，請使用 --help 查看說明。");
    return;
}

var opt = ((Parsed<Options>)result).Value;

// === 驗證必要參數 ===
if (string.IsNullOrWhiteSpace(opt.Source))
{
    Console.WriteLine("錯誤：--source 是必填參數。");
    return;
}

if (string.IsNullOrWhiteSpace(opt.Remote))
{
    Console.WriteLine("錯誤：--remote 是必填參數。");
    return;
}

if (string.IsNullOrWhiteSpace(opt.Config))
{
    Console.WriteLine("錯誤：--config 是必填參數。");
    return;
}

if (opt.Hangfire && string.IsNullOrWhiteSpace(opt.HfStorage))
{
    Console.WriteLine("錯誤：--hangfire=true 時，--hf-storage 是必填參數（pg 或 mssql）。");
    return;
}

// === 建立核心服務 ===
var archiver = new ArchiverService(opt);

// === 模式判斷 ===
if (!opt.Hangfire)
{
    // =============================================
    // 【模式一】單次執行（手動測試 / CI / 補跑）
    // =============================================
    Console.WriteLine();
    Console.WriteLine("=== [Mode: Manual] 單次執行模式 ===");
    Console.WriteLine();

    await archiver.ExecuteBackupAsync();

    Console.WriteLine();
    Console.WriteLine("執行完畢。");
}
else
{
    // =============================================
    // 【模式二】Hangfire 排程模式
    // =============================================
    Console.WriteLine();
    Console.WriteLine("=== [Mode: Hangfire] 排程執行模式 ===");
    Console.WriteLine();

    // 從環境變數讀取 Hangfire 連線字串
    var hfConn = Environment.GetEnvironmentVariable("HF_CONN");
    if (string.IsNullOrWhiteSpace(hfConn))
    {
        Console.WriteLine("錯誤：請設定環境變數 HF_CONN（Hangfire 資料庫連線字串）。");
        Console.WriteLine("  例如：HF_CONN=\"Server=localhost;Database=Hangfire;Integrated Security=true\"");
        Console.WriteLine("  或：HF_CONN=\"Host=192.168.1.200;Database=hangfire;Username=postgres;Password=xxx\"");
        return;
    }

    var builder = WebApplication.CreateBuilder(Array.Empty<string>());

    // === 設定 Hangfire 儲存 ===
    if (opt.HfStorage?.ToLower() == "mssql")
    {
        Console.WriteLine($"使用 MSSQL 作為 Hangfire 儲存...");
        builder.Services.AddHangfire(conf => conf.UseSqlServerStorage(hfConn));
    }
    else if (opt.HfStorage?.ToLower() == "pg")
    {
        Console.WriteLine($"使用 PostgreSQL 作為 Hangfire 儲存...");
        builder.Services.AddHangfire(conf => conf.UsePostgreSqlStorage(opt => opt.UseNpgsqlConnection(hfConn)));
    }
    else
    {
        Console.WriteLine("錯誤：--hf-storage 必須是 pg 或 mssql。");
        return;
    }

    builder.Services.AddHangfireServer();
    builder.Services.AddSingleton<ArchiverService>(archiver);

    var app = builder.Build();

    // === 設定 Hangfire Dashboard ===
    if (opt.HfDashboard)
    {
        // 注意：生產環境建議自行實作 IDashboardAuthorizationFilter 做驗證
        app.UseHangfireDashboard();
        Console.WriteLine($"Dashboard 已啟動：http://localhost:{opt.HfPort}/hangfire");
    }

    // === 手動 Trigger API ===
    app.MapGet("/trigger", (string? job) =>
    {
        string jobId;
        if (!string.IsNullOrEmpty(job))
        {
            RecurringJob.TriggerJob(job);
            jobId = $"Triggered recurring job: {job}";
        }
        else
        {
            jobId = BackgroundJob.Enqueue<ArchiverService>(s => s.ExecuteBackupAsync());
        }
        return Results.Ok(new { jobId });
    });

    // === 註冊定時任務 ===
    RecurringJob.AddOrUpdate<ArchiverService>(
        "DailyBackup",
        s => s.ExecuteBackupAsync(),
        opt.HfInterval
    );

    Console.WriteLine($"排程任務已註冊：Cron = {opt.HfInterval}");
    Console.WriteLine($"等待 Hangfire 觸發...");
    Console.WriteLine();

    // === 啟動 Web Server ===
    app.Run($"http://0.0.0.0:{opt.HfPort}");
}
