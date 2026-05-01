# BackupArchiver

定時將符合日期格式的資料夾壓縮並搬遷到網路儲存空間（SMB / NFS / SFTP / FTP / FTPS），支援 Hangfire 排程。

---

## 功能特色

- ✅ **單次執行**：手動測試、CI驗證、補跑
- ✅ **Hangfire 排程**：支援 MSSQL / PostgreSQL 儲存 Job 紀錄
- ✅ **網路傳輸**：全部使用 rclone（SMB / NFS / SFTP / FTP / FTPS）
- ✅ **多資料夾格式**：支援 `yyyymmdd` / `yyyy/MM/dd` / `yyyy/yyyymmdd` 等任意階層
- ✅ **跨平台**：.NET 8，Windows / Linux 都能跑
- ✅ **密碼安全**：rclone 密碼用 `rclone obscure` 加密

---

## 前置需求

1. **安裝 .NET 8 Runtime**
   - Windows: https://dotnet.microsoft.com/download/dotnet/8.0
   - Linux: `sudo apt install dotnet-sdk-8.0`

2. **安裝 rclone**（本機必須有 rclone command）
   ```bash
   # Linux/macOS
   curl https://rclone.org/install.sh | sudo bash

   # Windows
   # 下載 rclone.exe 至 C:\Tools\rclone.exe
   # 並加入 PATH
   ```

3. **設定 rclone config**
   ```bash
   rclone config
   # 依序設定你的 SMB / SFTP / FTP 等遠端
   # 密碼會加密存在 config 檔
   ```

---

## 參數說明

### 基本參數

| 參數 | 必填 | 預設值 | 說明 |
|------|------|--------|------|
| `--source` | ✅ | - | 來源根目錄 |
| `--days` | ✅ | - | 超過幾天要搬遷 |
| `--source-pattern` | | `yyyyMMdd` | 來源資料夾的日期格式 |
| `--remote` | ✅ | - | rclone remote + 目的地路徑，例如 `smb-daily:/archive/` |
| `--dest-pattern` | | `{date}` | 目的地資料夾格式 |
| `--config` | ✅ | - | rclone config 檔路徑 |
| `--compress` | | `true` | 是否壓縮成 zip |
| `--log` | | `backup.log` | 文字 Log 檔路徑 |

### Hangfire 參數

| 參數 | 必填 | 預設值 | 說明 |
|------|------|--------|------|
| `--hangfire` | | `false` | 是否啟用 Hangfire 排程模式 |
| `--hf-storage` | 當 hangfire=true | - | 資料庫類型：`pg` 或 `mssql` |
| `--hf-interval` | | `0 1 * * *` | Cron 表達式（預設每天凌晨 01:00）|
| `--hf-dashboard` | | `false` | 是否開啟 Dashboard |
| `--hf-port` | | `5000` | Dashboard port |

---

## 使用範例

### 單次執行（手動測試）

```bash
./BackupArchiver \
  --source "/backup/data" \
  --days 5 \
  --source-pattern "yyyy/MM/dd" \
  --remote "smb-daily:/archive/" \
  --dest-pattern "yyyy/MM/dd" \
  --config "/opt/tools/rclone.conf"
```

### Hangfire + PostgreSQL（每天凌晨 01:00）

```bash
export HF_CONN="Host=192.168.1.200;Database=hangfire;Username=postgres;Password=xxx"

./BackupArchiver \
  --source "/backup/data" \
  --days 5 \
  --source-pattern "yyyy/MM/dd" \
  --remote "smb-daily:/archive/" \
  --dest-pattern "yyyy/MM/dd" \
  --config "/opt/tools/rclone.conf" \
  --hangfire true \
  --hf-storage pg \
  --hf-interval "0 1 * * *"
```

### Hangfire + MSSQL（含 Dashboard）

```bash
set HF_CONN=Server=localhost;Database=Hangfire;Integrated Security=true

BackupArchiver.exe ^
  --source "D:\BackupData" ^
  --days 5 ^
  --source-pattern "yyyy/MM/dd" ^
  --remote "sftp-backup:/archive/" ^
  --dest-pattern "yyyy/MM/dd" ^
  --config "C:\Tools\rclone.conf" ^
  --hangfire true ^
  --hf-storage mssql ^
  --hf-dashboard true ^
  --hf-interval "0 1 * * *"
```

### Windows 工作排程範例

```
# 工作排程器設定
程式：C:\Tools\BackupArchiver.exe
引數：--source "D:\BackupData" --days 5 --source-pattern "yyyy/MM/dd" --remote "smb-daily:/archive/" --dest-pattern "yyyy/MM/dd" --config "C:\Tools\rclone.conf" --hangfire true --hf-storage mssql --hf-interval "0 1 * * *"
環境變數：HF_CONN = Server=localhost;Database=Hangfire;Integrated Security=true
```

---

## 支援的目錄結構

**來源結構（`--source-pattern`）：**

```
D:\BackupData\
├── 2024\05\20240501          # --source-pattern "yyyy/MM/dd"
├── 2024\05\20240502
└── 2025\01\20250101

# 或
D:\BackupData\
├── 2024\20240501              # --source-pattern "yyyy/dd"
└── 2025\20250101
```

**目的地結構（`--dest-pattern`）：**

```
smb-daily:/archive/
├── {date}/                    # --dest-pattern "{date}"
├── 2024/05/20240501/
└── 2025/01/20250101/

# 或保持扁平
smb-daily:/archive/
└── 20240501.zip              # --dest-pattern "{date}"
```

---

## rclone config 範本

```ini
[smb-daily]
type = smb
host = 192.168.1.100
user = backup_admin
pass = xxxxxENCRYPTEDxxxxx

[smb-weekly]
type = smb
host = 192.168.1.101
user = backup_admin
pass = xxxxxENCRYPTEDxxxxx

[sftp-daily]
type = sftp
host = 192.168.1.110
user = backup_user
pass = xxxxxENCRYPTEDxxxxx

[sftp-weekly]
type = sftp
host = 192.168.1.111
user = backup_user
pass = xxxxxENCRYPTEDxxxxx

[ftp-archive]
type = ftp
host = 192.168.1.120
user = ftp_user
pass = xxxxxENCRYPTEDxxxxx

[nfs-primary]
type = nfs
server = 192.168.1.130
path = /mnt/backup
```

### 加密密碼

```bash
# 產生加密密碼
rclone obscure "YOUR_PASSWORD"
# 輸出：xxxxxENCRYPTEDxxxxx

# 將輸出結果寫入 config 檔的 pass= 後面
```

---

## Log 格式

```
2026-05-01 09:30:00 | D:\Data\2024\05\20240501 -> smb-daily:/archive/2024/05/ | SUCCESS
2026-05-01 09:30:05 | D:\Data\2024\06\20240620 -> sftp-weekly:/archive/2024/06/ | SUCCESS
2026-05-02 09:25:00 | D:\Data\2024\07\20240715 -> smb-daily:/archive/2024/07/ | FAILED | network timeout
```

---

## 建置

```bash
# 建置（Windows / Linux 通用）
dotnet build -c Release

# 發布 self-contained（包含 .NET Runtime）
dotnet publish -c Release -r win-x64 --self-contained true
dotnet publish -c Release -r linux-x64 --self-contained true
```

---

## 授權

MIT License
