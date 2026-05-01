# ColdDataMigrator

定時將符合日期格式的資料夾壓縮並搬遷到網路儲存空間（SMB / NFS / SFTP / FTP / FTPS），支援 Hangfire 排程與 Job Dashboard。

---

## 功能特色

- ✅ **單次執行**：手動測試、CI驗證、補跑
- ✅ **Hangfire 排程**：支援 PostgreSQL / MSSQL 儲存 Job 紀錄
- ✅ **Hangfire Dashboard**：視覺化查看所有 Job 執行歷程
- ✅ **手動觸發 API**：`POST /trigger` 隨時手動執行
- ✅ **網路傳輸**：全部使用 rclone（SMB / NFS / SFTP / FTP / FTPS）
- ✅ **多資料夾格式**：支援 `yyyymmdd` / `yyyy/MM/dd` / `yyyy/yyyymmdd` 等任意階層
- ✅ **跨平台**：.NET 8，Windows / Linux 都能跑
- ✅ **密碼安全**：rclone 密碼用 `rclone obscure` 加密

---

## 前置需求

### 1. 安裝 .NET 8 SDK

```bash
# Linux (Ubuntu/Debian)
sudo apt update && sudo apt install -y dotnet-sdk-8.0

# macOS
brew install dotnet

# Windows
# https://dotnet.microsoft.com/download/dotnet/8.0
```

### 2. 安裝 rclone（本機必須有 rclone command）

```bash
# Linux/macOS
curl https://rclone.org/install.sh | sudo bash

# Windows
# 下載 rclone.exe 至 C:\Tools\rclone.exe 並加入 PATH
```

### 3. 設定 rclone config

```bash
rclone config
# 依序設定你的 SMB / SFTP / FTP 等遠端
# 密碼會加密存在 config 檔
```

---

## 快速開始

### Step 1：Clone 並建置

```bash
git clone https://github.com/sm245735/ColdDataMigrator.git
cd ColdDataMigrator
dotnet build -c Release
```

### Step 2：設定 rclone（以 SMB 為例）

```bash
rclone config
# 依序回答：
# n) New remote → name: smb-daily → 類型: smb
# host: 192.168.1.100
# user: backup_admin
# password: (輸入密碼，rclone 會幫你加密)
```

### Step 3：建立資料夾結構測試

```bash
mkdir -p /tmp/test_archive/2024/05/20240501
mkdir -p /tmp/test_archive/2024/05/20240502
mkdir -p /tmp/test_archive/2024/06/20240615
# 寫入一些測試資料
echo "test data" > /tmp/test_archive/2024/05/20240501/file1.txt
echo "test data" > /tmp/test_archive/2024/05/20240502/file2.txt
echo "test data" > /tmp/test_archive/2024/06/20240615/file3.txt
```

### Step 4：單次執行（手動測試）

```bash
./BackupArchiver \
  --source /tmp/test_archive \
  --days 3 \
  --source-pattern "yyyy/MM/dd" \
  --remote smb-daily:/share \
  --config ~/.config/rclone/rclone.conf \
  --compress true \
  --log /tmp/backup.log
```

> 超過 3 天的資料夾（20240501、20240502）會被壓縮搬遷，20240615 還沒超過 3 天所以保留。

### Step 5：啟用 Hangfire 排程

```bash
# 設定資料庫連線（PostgreSQL）
export HF_CONN="Host=192.168.1.200;Database=hangfire;Username=postgres;Password=xxx"

./BackupArchiver \
  --source /tmp/test_archive \
  --days 3 \
  --source-pattern "yyyy/MM/dd" \
  --remote smb-daily:/share \
  --config ~/.config/rclone/rclone.conf \
  --compress true \
  --log /tmp/backup.log \
  --hangfire true \
  --hf-storage pg \
  --hf-dashboard true \
  --hf-port 5000
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

## API 端點

當 `--hangfire true` 啟用時可用：

| 方法 | 路徑 | 說明 |
|------|------|------|
| `POST` | `/trigger` | 手動觸發一次備份任務 |
| `POST` | `/trigger?job=JobName` | 觸發特定命名的週期任務 |
| `GET` | `/hangfire` | Hangfire Dashboard |

---

## 支援的目錄結構

### 來源結構（`--source-pattern`）

```
/backup/data/
├── 2024/05/20240501          # --source-pattern "yyyy/MM/dd"
├── 2024/05/20240502
└── 2025/01/20250101

# 或
/backup/data/
├── 2024/20240501             # --source-pattern "yyyy/dd"
└── 2025/20250101
```

### 目的地結構（`--dest-pattern`）

```
smb-daily:/archive/
├── {date}/                   # --dest-pattern "{date}"
├── 2024/05/20240501/
└── 2025/01/20250101/

# 或保持扁平
smb-daily:/archive/
└── 20240501.zip             # --dest-pattern "{date}"
```

---

## rclone config 範本

```ini
[smb-daily]
type = smb
host = 192.168.1.100
user = backup_admin
pass = xxxxxENCRYPTEDxxxxx

[sftp-backup]
type = sftp
host = 192.168.1.110
user = backup_user
pass = xxxxxENCRYPTEDxxxxx

[ftp-archive]
type = ftp
host = 192.168.1.120
user = ftp_user
pass = xxxxxENCRYPTEDxxxxx
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
2026-05-01 09:30:00 | /data/2024/05/20240501 -> smb-daily:/archive/2024/05/ | SUCCESS
2026-05-01 09:30:05 | /data/2024/06/20240620 -> sftp-backup:/archive/2024/06/ | SUCCESS
2026-05-02 09:25:00 | /data/2024/07/20240715 -> smb-daily:/archive/2024/07/ | FAILED | network timeout
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

## 專案結構

```
ColdDataMigrator/
├── Program.cs           # 進入點、CLI 參數、Hangfire 設定
├── ArchiverService.cs   # 核心邏輯：掃描、壓縮、搬遷
├── Options.cs           # CLI 參數模型
├── BackupArchiver.csproj
├── README.md            # 本文件
├── TUTORIAL.md         # 詳細教學（Q&A 形式）
└── .gitignore
```

---

## 授權

MIT License
