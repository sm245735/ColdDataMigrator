using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace BackupArchiver;

public class ArchiverService
{
    private readonly Options _opt;
    private readonly string[] _datePatterns;

    public ArchiverService(Options opt)
    {
        _opt = opt;

        // 將 source-pattern 轉成日期欄位的比對 Pattern
        // 例如 "yyyy/MM/dd" → ["\\d{4}", "\\d{2}", "\\d{2}"]
        _datePatterns = opt.SourcePattern
            .Split('/', '\\')
            .Select(p => Regex.Replace(p, "yyyy", @"\d{4}")
                              .Replace("MM", @"\d{2}")
                              .Replace("dd", @"\d{2}"))
            .ToArray();
    }

    public async Task ExecuteBackupAsync()
    {
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 開始備份任務...");
        Console.WriteLine($"  來源：{_opt.Source}");
        Console.WriteLine($"  目標：{_opt.Remote}");
        Console.WriteLine($"  保留天數：{_opt.Days} 天");

        // 1. 掃描符合日期格式的資料夾
        var targetDirs = ScanDateFolders(_opt.Source, _opt.Days);

        if (targetDirs.Count == 0)
        {
            Console.WriteLine("  沒有找到需要搬遷的資料夾。");
            WriteLog(null, "SKIP", "沒有找到需要搬遷的資料夾");
            return;
        }

        Console.WriteLine($"  找到 {targetDirs.Count} 個資料夾需要處理。");

        // 2. 依序處理
        foreach (var dir in targetDirs)
        {
            try
            {
                await ProcessFolderAsync(dir);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [錯誤] 處理 {dir} 失敗：{ex.Message}");
                WriteLog(dir, "FAILED", ex.Message);
            }
        }

        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 備份任務完成。");
    }

    /// <summary>
    /// 掃描並過濾出符合日期格式且超過 N 天的資料夾
    /// </summary>
    private List<string> ScanDateFolders(string rootPath, int daysThreshold)
    {
        var result = new List<string>();
        var cutoffDate = DateTime.Now.AddDays(-daysThreshold).Date;

        // 正規表達式：整條路徑的最後 N 層要符合日期格式
        var regexPattern = "^" + string.Join("[\\\\/]", _datePatterns) + "$";

        if (!Directory.Exists(rootPath))
        {
            Console.WriteLine($"  [警告] 來源目錄不存在：{rootPath}");
            return result;
        }

        // 遞迴掃描所有子資料夾
        foreach (var dir in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories))
        {
            // 取相對路徑
            var relPath = Path.GetRelativePath(rootPath, dir);
            var parts = relPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // 只檢查最後 N 層是否符合日期格式
            if (parts.Length < _datePatterns.Length) continue;

            var dateParts = parts.Skip(parts.Length - _datePatterns.Length).ToArray();
            var dateStr = string.Join(Path.DirectorySeparatorChar, dateParts);

            if (!Regex.IsMatch(dateStr, regexPattern)) continue;

            // 解析日期
            if (!TryParseDate(dateParts, out var dirDate)) continue;

            // 判斷是否超過門檻
            if (dirDate < cutoffDate)
            {
                result.Add(dir);
            }
        }

        return result;
    }

    /// <summary>
    /// 從路徑片段解析日期
    /// </summary>
    private bool TryParseDate(string[] dateParts, out DateTime date)
    {
        date = default;

        var patternParts = _opt.SourcePattern.Split('/', '\\');

        // 根據 source-pattern 的順序重建日期字串
        var dateStr = string.Empty;

        if (patternParts.Length == 1 && patternParts[0].Length >= 8)
        {
            // 沒有分隔符的情況（例如 "yyyyMMdd"），直接串所有 dateParts
            dateStr = string.Join("", dateParts);
        }
        else
        {
            // 有分隔符的情況（例如 "yyyy/MM/dd"）
            for (int i = 0; i < patternParts.Length; i++)
            {
                if (i >= dateParts.Length) break;
                var part = patternParts[i];
                if (part == "yyyy" || part == "MM" || part == "dd")
                    dateStr += dateParts[i];
            }
        }

        // dateStr 應該是 "yyyyMMdd" 或 "yyyyMMddHHmmss" 之類
        if (dateStr.Length < 8) return false;

        var dateOnly = dateStr.Substring(0, 8);
        if (DateTime.TryParseExact(dateOnly, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out date))
            return true;

        return false;
    }

    /// <summary>
    /// 處理單一資料夾：壓縮 → rclone move → 刪除 → Log
    /// </summary>
    private async Task ProcessFolderAsync(string folderPath)
    {
        var folderName = Path.GetFileName(folderPath);
        var zipPath = folderPath + ".zip";

        // 2a. 壓縮
        if (_opt.Compress)
        {
            Console.WriteLine($"  壓縮：{folderName}");
            ZipFile.CreateFromDirectory(folderPath, zipPath, CompressionLevel.Optimal, false);
        }
        else
        {
            zipPath = folderPath; // 不壓縮，直接搬
        }

        // 2b. rclone move
        Console.WriteLine($"  搬遷：{folderName} -> {_opt.Remote}");

        // 解析目的地路徑，替換 {date}
        var destPath = BuildDestPath(folderPath);
        var rcloneDest = $"{_opt.Remote}/{destPath}";

        var success = await RunRcloneMoveAsync(zipPath, rcloneDest);

        if (!success)
        {
            // rclone 失敗，刪除暫時的 zip
            if (_opt.Compress && File.Exists(zipPath))
                File.Delete(zipPath);
            throw new Exception("rclone move 失敗");
        }

        // 2c. 刪除原資料夾
        if (_opt.Compress)
        {
            // zip 已搬走，資料夾可以刪了
            Directory.Delete(folderPath, true);
            // 刪除暫時的 zip（rclone move 已搬走）
            if (File.Exists(zipPath)) File.Delete(zipPath);
        }
        else
        {
            // 不壓縮：已經被 rclone 搬走了，但原資料夾可能還在（視 rclone 實作）
            // 這裡手動刪除原資料夾
            Directory.Delete(folderPath, true);
        }

        WriteLog(folderPath, "SUCCESS", $"已搬遷至 {rcloneDest}");
        Console.WriteLine($"  完成：{folderName}");
    }

    /// <summary>
    /// 根據 dest-pattern 組出目的地子路徑
    /// </summary>
    private string BuildDestPath(string folderPath)
    {
        var relPath = Path.GetRelativePath(_opt.Source, folderPath);
        var parts = relPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // 取出日期部分
        if (!TryParseDate(parts.Skip(parts.Length - _datePatterns.Length).ToArray(), out var date))
            return _opt.DestPattern;

        var result = _opt.DestPattern
            .Replace("{date}", date.ToString("yyyyMMdd"))
            .Replace("yyyy", date.ToString("yyyy"))
            .Replace("MM", date.ToString("MM"))
            .Replace("dd", date.ToString("dd"));

        return result;
    }

    /// <summary>
    /// 執行 rclone move
    /// </summary>
    private async Task<bool> RunRcloneMoveAsync(string source, string dest)
    {
        var args = $"move \"{source}\" \"{dest}\" --config \"{_opt.Config}\" --progress";

        Console.WriteLine($"    rclone {args}");

        var psi = new ProcessStartInfo
        {
            FileName = "rclone",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) return false;

        var output = await proc.StandardOutput.ReadToEndAsync();
        var error = await proc.StandardError.ReadToEndAsync();

        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
        {
            Console.WriteLine($"    rclone 錯誤：{error}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 寫入文字 Log
    /// </summary>
    private void WriteLog(string? folderPath, string status, string message)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var remote = _opt.Remote;
            var logLine = $"{timestamp} | {folderPath ?? "(n/a)"} -> {remote} | {status} | {message}{Environment.NewLine}";
            File.AppendAllText(_opt.Log, logLine);
        }
        catch
        {
            // Log 失敗不影響主要流程
        }
    }
}
