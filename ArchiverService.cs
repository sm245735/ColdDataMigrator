using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using log4net;
using log4net.Config;

namespace BackupArchiver;

public class ArchiverService
{
    private readonly Options _opt;
    private readonly string[] _datePatterns;
    private readonly List<Regex> _excludeRegexes;
    private static readonly ILog _log = LogManager.GetLogger(typeof(ArchiverService));

    static ArchiverService()
    {
        // 建立 logs 目錄（log4net 的 RollingFileAppender 需要）
        Directory.CreateDirectory("logs");
        // 從 log4net.config 初始化（只在第一次類別載入時執行）
        var logRepo = LogManager.CreateRepository("BackupArchiver");
        XmlConfigurator.Configure(logRepo, new FileInfo("log4net.config"));
    }

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

        // 編譯排除規則（glob * → regex .*）
        _excludeRegexes = opt.ExcludePatterns
            .Select(p => new Regex("^" + Regex.Escape(p).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase))
            .ToList();
    }

    public async Task ExecuteBackupAsync()
    {
        _log.Info("========================================");
        _log.Info($"開始備份任務 來源：{_opt.Source}  目標：{_opt.Remote}  保留：{_opt.Days} 天");
        if (_opt.DryRun)
            _log.Info("預覽模式：ON（不會實際修改任何檔案）");

        // 1. 掃描符合日期格式的資料夾
        var targetDirs = ScanDateFolders(_opt.Source, _opt.Days);

        if (targetDirs.Count == 0)
        {
            _log.Warn("沒有找到需要搬遷的資料夾。");
            WriteLog(null, "SKIP", "沒有找到需要搬遷的資料夾");
            return;
        }

        _log.Info($"找到 {targetDirs.Count} 個資料夾需要處理。");

        // 2. 預覽模式
        if (_opt.DryRun)
        {
            _log.Info("=== [Dry-Run 預覽] 以下資料夾將被處理 ===");
            foreach (var dir in targetDirs)
            {
                var relPath = Path.GetRelativePath(_opt.Source, dir);
                _log.Info($"  [預覽] {relPath}");
            }
            _log.Info($"共 {targetDirs.Count} 個資料夾（預覽模式，未實際執行）");
            return;
        }

        // 3. 依序處理（顯示進度條）
        int total = targetDirs.Count;
        int done = 0;
        int success = 0;
        int failed = 0;

        foreach (var dir in targetDirs)
        {
            var relPath = Path.GetRelativePath(_opt.Source, dir);
            DrawProgressBar(done, total, relPath);

            try
            {
                await ProcessFolderAsync(dir);
                success++;
            }
            catch (Exception ex)
            {
                _log.Error($"處理 {dir} 失敗：{ex.Message}");
                WriteLog(dir, "FAILED", ex.Message);
                failed++;
            }

            done++;
            DrawProgressBar(done, total, relPath);
            Console.WriteLine(); // 換行，下一個迴圈會在同位置重繪
        }

        // 最終完成狀態
        _log.Info($"備份任務完成。總計：{total} | 成功：{success} | 失敗：{failed}");
        _log.Info("========================================");
    }

    /// <summary>
    /// 掃描並過濾出符合日期格式且超過 N 天的資料夾
    /// </summary>
    internal List<string> ScanDateFolders(string rootPath, int daysThreshold)
    {
        var result = new List<string>();
        var cutoffDate = DateTime.Now.AddDays(-daysThreshold).Date;

        // 正規表達式：整條路徑的最後 N 層要符合日期格式
        var regexPattern = "^" + string.Join("[\\\\/]", _datePatterns) + "$";

        if (!Directory.Exists(rootPath))
        {
            _log.Warn($"來源目錄不存在：{rootPath}");
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

            // 檢查是否在排除名單中（folderName 或完整相對路徑）
            var folderName = parts[^1];
            var fullRelPath = relPath.Replace(Path.DirectorySeparatorChar, '/');
            if (_excludeRegexes.Count > 0 &&
                (_excludeRegexes.Any(r => r.IsMatch(folderName)) || _excludeRegexes.Any(r => r.IsMatch(fullRelPath))))
                continue;

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
                if (part.ToLower() == "yyyy")
                    dateStr += dateParts[i];
                else if (part.ToLower() == "mm")
                    dateStr += dateParts[i];
                else if (part.ToLower() == "dd")
                    // dd 只取末 2 碼（例如 "20260421" → 取 "21"）
                    dateStr += dateParts[i][^2..];
                else if (part.ToLower() == "yyyymmdd")
                    // yyyyMMdd 片段只取末 2 碼當 dd（例如 "20260430" → 取 "30"）
                    dateStr += dateParts[i][^2..];
            }
        }

        // dateStr 應該是 "yyyyMMdd" 或 "yyyyMMddHHmmss" 之類
        // 取前 8 碼當日期（即使後面有多餘資料也能正確截斷）
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
        string zipPath;

        // 2a. 壓縮（檢查目的地是否已存在）
        if (_opt.Compress)
        {
            zipPath = folderPath + ".zip";

            try
            {
                _log.Info($"壓縮：{folderName}");
                // ZipFile.CreateFromDirectory(folderPath, zipPath, CompressionLevel.Optimal, false);
            }
            catch (Exception ex)
            {
                throw new Exception($"壓縮失敗：{ex.Message}");
            }
        }
        else
        {
            zipPath = folderPath; // 不壓縮，直接搬
        }

        // 2b. rclone move
        _log.Info($"搬遷：{folderName} -> {_opt.Remote}");

        var destPath = BuildDestPath(folderPath);
        var rcloneDest = $"{_opt.Remote}/{destPath}";

        var success = await RunRcloneMoveAsync(zipPath, rcloneDest);

        if (!success)
        {
            // rclone 失敗，嘗試刪除暫時的 zip
            try
            {
                if (_opt.Compress && File.Exists(zipPath))
                    File.Delete(zipPath);
            }
            catch { /* 忽略刪除失敗 */ }

            throw new Exception("rclone move 失敗");
        }

        // 2c. 刪除原資料夾 / 暫時 zip
        try
        {
            if (_opt.Compress)
            {
                // zip 已搬走，刪除暫時的 zip（rclone move 已完成）
                if (File.Exists(zipPath))
                    File.Delete(zipPath);
            }

            // 刪除原資料夾
            if (Directory.Exists(folderPath))
                Directory.Delete(folderPath, true);
        }
        catch (Exception ex)
        {
            // 刪除失敗不影響整體流程，僅寫入警告
            _log.Warn($"清理失敗（{folderName}）：{ex.Message}");
            WriteLog(folderPath, "WARN", $"搬遷成功但清理失敗：{ex.Message}");
            return;
        }

        WriteLog(folderPath, "SUCCESS", $"已搬遷至 {rcloneDest}");
        _log.Info($"完成：{folderName}");
    }

    /// <summary>
    /// 根據 dest-pattern 組出目的地子路徑
    /// </summary>
    internal string BuildDestPath(string folderPath)
    {
        var relPath = Path.GetRelativePath(_opt.Source, folderPath);
        var parts = relPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar,
                                  StringSplitOptions.RemoveEmptyEntries);

        // 只取最後 N 層（N = source-pattern 的日期階層數）
        if (parts.Length < _datePatterns.Length)
            return _opt.DestPattern;

        var dateParts = parts.Skip(parts.Length - _datePatterns.Length).ToArray();

        // 使用 TryParseDate 解析正確的日期
        if (!TryParseDate(dateParts, out var parsedDate))
            return _opt.DestPattern;

        // 置換 dest-pattern：先把 yyyyMMdd 換成 \b（退格字元，路徑不可能出現），
        // 避免 "202604" 這類子字串被 individual yyyy/MM/dd 置換時吃錯
        var result = _opt.DestPattern
            .Replace("yyyyMMdd", "\b")
            .Replace("yyyy", parsedDate.ToString("yyyy"))
            .Replace("MM", parsedDate.ToString("MM"))
            .Replace("dd", parsedDate.ToString("dd"))
            .Replace("\b", parsedDate.ToString("yyyyMMdd"));

        return result;
    }

    /// <summary>
    /// 執行 rclone move
    /// </summary>
    private async Task<bool> RunRcloneMoveAsync(string source, string dest)
    {
        dest = dest.Replace(@"\", "/");
        var args = $"move \"{source}\" \"{dest}\" --config \"{_opt.Config}\" --progress --verbose";

        _log.Debug($"rclone {args}");

        ProcessStartInfo psi;
        Process? proc;

        try
        {
            psi = new ProcessStartInfo
            {
                FileName = "rclone",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            _log.Error($"rclone 啟動失敗：{ex.Message}");
            return false;
        }

        if (proc == null)
        {
            _log.Error("rclone 啟動失敗：Process 為 null");
            return false;
        }

        try
        {
            // 讓輸出顯示在主控台
            proc.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Console.WriteLine(e.Data); // 即時印出 rclone 的進度
                }
            };

            proc.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Console.WriteLine($"[錯誤或日誌] {e.Data}");
                }
            };

            // 開始非同步讀取
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // 等待程式執行完畢
            await proc.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            _log.Error($"rclone 讀取輸出失敗：{ex.Message}");
            try { proc.Kill(); } catch { }
            return false;
        }

        if (proc.ExitCode != 0)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 繪製進度條（在同一行覆寫）
    /// </summary>
    private void DrawProgressBar(int current, int total, string currentItem)
    {
        const int barWidth = 40;
        double fraction = total > 0 ? (double)current / total : 0;
        int filled = (int)(fraction * barWidth);
        int remaining = barWidth - filled;

        string pct = total > 0 ? $"{fraction * 100,5:F1}" : "N/A ";
        string bar = new string('=', filled) + new string('-', remaining);

        // 移動到行首、清除整行、寫入新內容（不換行）
        Console.Write($"\r  [{bar}] {pct}% ({current}/{total}) {TruncatePath(currentItem, 30)}");

        if (current == 0) Console.WriteLine(); // 第一筆先換行
    }

    /// <summary>
    /// 截斷路徑，保留頭尾
    /// </summary>
    private string TruncatePath(string path, int maxLen)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= maxLen) return path;
        if (maxLen <= 10) return path.Substring(0, maxLen);
        int keep = maxLen - 3;
        return path.Substring(0, keep / 2) + "..." + path.Substring(path.Length - (keep - keep / 2));
    }

    /// <summary>
    /// 寫入文字 Log（純文字格式，與 log4net 分開）
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

    /// <summary>
    /// 寫入系統日誌（STARTUP / STARTUP_ERROR 等）
    /// </summary>
    public void WriteSystemLog(string status, string message)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var logLine = $"{timestamp} | SYSTEM -> (n/a) | {status} | {message}{Environment.NewLine}";
            File.AppendAllText(_opt.Log, logLine);
        }
        catch
        {
            // Log 失敗不影響主要流程
        }
    }
}
