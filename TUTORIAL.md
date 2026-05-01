# ColdDataMigrator 詳細教學

以問答形式說明常見情境，適用於 Windows / Linux 雙平台。

---

## Q1：如何確認我的資料夾格式是否支援？

### 常用的日期格式組合

| 資料夾範例 | `--source-pattern` |
|-----------|-------------------|
| `20240501` | `yyyyMMdd` |
| `2024/05/01` | `yyyy/MM/dd` |
| `2024/0501` | `yyyy/MMdd` |
| `2024-05-01` | `yyyy-MM-dd` |
| `20240501_backup` | `yyyyMMdd'_backup'` |

### 測試你的格式

把 `--days 0`（今天以內）和 `--compress false`（不壓縮）搭配，執行一次看log輸出是否正確抓到資料夾：

```bash
./BackupArchiver \
  --source "/path/to/your/data" \
  --days 0 \
  --source-pattern "yyyy/MM/dd" \
  --remote smb-daily:/test \
  --config ~/.config/rclone/rclone.conf \
  --compress false \
  --log /tmp/test.log

# 檢查 log
cat /tmp/test.log
```

---

## Q2：rclone password 怎麼設定才安全？

### Step 1：不要用明文密碼

```bash
# 錯誤示範（密碼會殘留在 shell history）
rclone config
# password: MyPlainPassword123

# 正確：用 rclone obscure 加密
rclone obscure "MyPlainPassword123"
# 輸出：xxxxxENCRYPTEDxxxxx
```

### Step 2：手動寫入 config 檔

```bash
nano ~/.config/rclone/rclone.conf
```

```ini
[smb-daily]
type = smb
host = 192.168.1.100
user = backup_admin
pass = xxxxxENCRYPTEDxxxxx   # ← 貼上加密後的密文
```

### Step 3：設定 config 檔權限（Linux）

```bash
chmod 600 ~/.config/rclone/rclone.conf
```

---

## Q3：PostgreSQL 的 Hangfire 資料庫要怎麼建立？

### 登入 PostgreSQL 建立資料庫

```bash
psql -h 192.168.1.200 -U postgres -d postgres
```

```sql
-- 建立專用資料庫
CREATE DATABASE hangfire;

-- 建立專用帳號（可選，更安全）
CREATE USER hf_admin WITH PASSWORD 'YourStrongPassword';
GRANT ALL PRIVILEGES ON DATABASE hangfire TO hf_admin;
\c hangfire
GRANT ALL ON SCHEMA public TO hf_admin;
```

### 連線字串格式

```bash
# Linux/macOS（export）
export HF_CONN="Host=192.168.1.200;Port=5432;Database=hangfire;Username=hf_admin;Password=YourStrongPassword"

# Windows（set）
set HF_CONN=Host=192.168.1.200;Port=5432;Database=hangfire;Username=hf_admin;Password=YourStrongPassword
```

### 如何確認資料庫已經建立好 Tables？

第一次啟動 Hangfire 時，會自動建立以下 Tables：
`aggregatedcounter`、`counter`、`hash`、`job`、`jobparameter`、`jobqueue`、`list`、`lock`、`schema`、`server`、`set`、`state`

查詢確認：
```bash
psql -h 192.168.1.200 -U hf_admin -d hangfire -c "\dt"
```

---

## Q4：MSSQL 的 Hangfire 資料庫要怎麼建立？

### 在 SQL Server 建立資料庫

```sql
-- SSMS 或 sqlcmd 執行
CREATE DATABASE Hangfire;
GO
```

### 連線字串格式

```bash
# Windows（整合式驗證，AD 帳號）
set HF_CONN=Server=localhost;Database=Hangfire;Integrated Security=true

# 或 SQL 帳號驗證
set HF_CONN=Server=localhost;Database=Hangfire;User Id=hangfire_user;Password=YourStrongPassword
```

---

## Q5：Hangfire Dashboard 怎麼連？會被別人看到嗎？

### 預設只綁定本機（安全）

```bash
--hf-dashboard true --hf-port 5000
# 預設 listen 0.0.0.0:5000 → 生產環境建議用反向代理或 VPN
```

### 從本機電腦存取

瀏覽器開啟：
```
http://<伺服器IP>:5000/hangfire
```

### 生產環境加強安全（可選）

```bash
# 用 Nginx 反向代理，限制只有特定 IP 可存取
location /hangfire {
    proxy_pass http://127.0.0.1:5000/hangfire;
    allow 192.168.1.0/24;   # 限制公司內網
    deny all;
}
```

---

## Q6：如何手動觸發一次備份？

### 方式一：API（推薦，生產環境常用）

```bash
# 觸發預設備份任務
curl -X POST http://localhost:5000/trigger

# 觸發特定命名的週期任務
curl -X POST "http://localhost:5000/trigger?job=DailyBackup"
```

### 方式二：Hangfire Dashboard 手動 Enqueue

1. 開啟 `http://localhost:5000/hangfire`
2. 點左側選單 `Enqueued` → `backup-daily`
3. 點 `Enqueue New Job` → 確認執行

---

## Q7：備份失敗了怎麼補跑？

### 確認失敗的任務

開啟 Hangfire Dashboard → 左側 `Failed` 列表

### 方式一：Retry 單一任務

Dashboard → 點進該失敗任務 → 點 `Retry`

### 方式二：補跑整個週期（適用於網路中斷後）

```bash
# 重新觸發一次（會跳過正在排隊的任務）
curl -X POST http://localhost:5000/trigger
```

### 方式三：手動執行單次（不用 Hangfire）

```bash
# 用 --days 0 測試今天以內的資料夾
# 補跑特定日期區間：
# 1. 先查出要補跑的資料夾（看log）
# 2. 設定 --days N 撈回 N 天前還沒搬的
./BackupArchiver \
  --source "/path/to/data" \
  --days 10 \
  --source-pattern "yyyy/MM/dd" \
  --remote smb-daily:/archive \
  --config ~/.config/rclone/rclone.conf \
  --compress true \
  --log /tmp/backup-retry.log
```

---

## Q8：如何調整執行時間（Cron 語法）？

### 常用 Cron 格式

| 表達式 | 意義 |
|--------|------|
| `0 1 * * *` | 每天凌晨 01:00 |
| `0 2 * * 1` | 每週一凌晨 02:00 |
| `0 3 1 * *` | 每月 1 日凌晨 03:00 |
| `0 */4 * * *` | 每 4 小時一次 |
| `30 23 * * *` | 每天 23:30 |

### 線上驗證工具

https://crontab.guru/

### 套用到 ColdDataMigrator

```bash
./BackupArchiver \
  ...其他參數... \
  --hangfire true \
  --hf-interval "0 2 * * 1"   # 改成每週一凌晨 02:00
```

---

## Q9：可以同時跑多個不同來源的任務嗎？

### 方式一：多個執行個體（各自不同的 --hf-port）

```bash
# 第一組：備份資料來源A
./BackupArchiver \
  --source /data/sourceA \
  --days 5 \
  --remote smb-a:/archive \
  --config ~/.config/rclone/rclone.conf \
  --hangfire true \
  --hf-port 5001              # 不同的 port

# 第二組：備份資料來源B
./BackupArchiver \
  --source /data/sourceB \
  --days 3 \
  --remote smb-b:/archive \
  --config ~/.config/rclone/rclone.conf \
  --hangfire true \
  --hf-port 5002              # 不同的 port
```

### 方式二：同一個執行個體，跑多個 Cron Job

修改 `Program.cs`，在 `AddRecurringJob` 段落新增多個 Job：

```csharp
// Job A：每天 01:00
app.Services.GetRequiredService<IBackgroundJobClient>().AddOrUpdate(
    "BackupSourceA",
    Job.Create(() => Console.WriteLine("SourceA Backup Triggered")),
    "0 1 * * *"
);

// Job B：每週一 02:00
app.Services.GetRequiredService<IBackgroundJobClient>().AddOrUpdate(
    "BackupSourceB",
    Job.Create(() => Console.WriteLine("SourceB Backup Triggered")),
    "0 2 * * 1"
);
```

---

## Q10：如何確認壓縮/搬遷確實成功？

### 查看 Log 檔

```bash
tail -f /tmp/backup.log
```

### 登入 NAS/SMB 確認檔案

```bash
# 用 rclone 確認
rclone ls smb-daily:/archive --config ~/.config/rclone/rclone.conf

# 或掛載後直接 ls
ls -la /mnt/smb/backup/
```

### 查看 Hangfire 歷史

Dashboard → 左側 `Succeeded` → 確認 Completed 和 Duration

---

## Q11：目標 NAS 空間不夠了怎麼辦？

### 搬遷前檢查空間（使用 rclone）

```bash
# 查看遠端可用空間
rclone about smb-daily: --config ~/.config/rclone/rclone.conf
```

### 建議加上 --dry-run 測試

```bash
> ✅ **目前版本已支援 `--dry-run`**，可安心使用。
# 建議先用 --days 0 --compress false 測試，看 log 輸出多少資料
```

---

## Q12：搬家到新伺服器要注意什麼？

### 1. 轉移 rclone config

```bash
# 舊伺服器
cat ~/.config/rclone/rclone.conf

# 新伺服器
nano ~/.config/rclone/rclone.conf  # 貼上並 chmod 600
```

### 2. 確認 rclone 版本

```bash
rclone version
```

### 3. 確認 .NET 8 SDK

```bash
dotnet --version   # 必須 >= 8.0
```

### 4. 確認網路芳鄰/ SMB 可正常存取

```bash
# 測試 SMB 連線
smbclient -L //192.168.1.100 -U backup_admin
```

### 5. 確認 HF_CONN 可連到同一個資料庫

```bash
# 如果用同一個 PostgreSQL/MSSQL，Job 歷史會保留下來
psql "$HF_CONN" -c "SELECT COUNT(*) FROM hangfire.job"
```

---

## Q13：Windows 工作排程器 vs Hangfire，該用哪個？

| 情境 | 建議 |
|------|------|
| 只有一台機器，簡單排程 | Windows 工作排程器（不用 Hangfire） |
| 需要視覺化 Job 歷史 | Hangfire Dashboard |
| 需要 API 手動觸發 | Hangfire |
| 多台伺服器統一管理 | Hangfire + 同一個 PostgreSQL |
| 要在 .NET 程式碼裡控制排程 | Hangfire |
| 只是簡單每天跑一次 | 工作排程器 + 本工具的單次模式 |

### Windows 工作排程器設定範例

```
程式：C:\Tools\ColdDataMigrator\BackupArchiver.exe
引數：--source "D:\BackupData" --days 5 --remote "smb-daily:/archive/" --config "C:\Tools\rclone.conf" --compress true --log "C:\Logs\backup.log"
觸發：每日 01:00
```

---

## Q14：如何卸載 / 停止服務？

### Hangfire 排程模式

直接停掉程式即可（Ctrl+C 或 kill），不會影響已經在執行的任務。

```bash
# 查看 PID
ps aux | grep BackupArchiver

# 優雅停止（推薦）
kill -SIGTERM <PID>

# 強制停止（如果優雅停止失敗）
kill -SIGKILL <PID>
```

### Windows 服務模式

```powershell
Stop-Service -Name "ColdDataMigrator"
Uninstall-ScheduledTask -TaskName "ColdDataMigratorDaily"
```

---

## Q15：如何更新新版本？

```bash
cd ColdDataMigrator
git pull origin main

# 重新建置
dotnet build -c Release

# 重啟服務
# （如果是 systemd service）
sudo systemctl restart cold-data-migrator
```
