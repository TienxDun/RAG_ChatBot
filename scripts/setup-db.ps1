$ErrorActionPreference = 'Stop'

# Đường dẫn tới file .env
$envFile = Join-Path $PSScriptRoot "..\.env"
$saPassword = ""

# Đọc mật khẩu từ file .env
if (Test-Path $envFile) {
    $content = Get-Content $envFile
    foreach ($line in $content) {
        if ($line -match "^MSSQL_SA_PASSWORD=(.*)$") {
            $saPassword = $matches[1].Trim()
            break
        }
    }
}

# Nếu không tìm thấy trong .env, dùng mặc định
if (-not $saPassword) {
    $saPassword = "YourStrong@Password123"
}

Write-Host "--- Khởi tạo Database SQL Server ---" -ForegroundColor Cyan

# Kiểm tra container có đang chạy không
$containerStatus = docker inspect -f '{{.State.Running}}' sqlserver-db 2>$null
if ($containerStatus -ne "true") {
    Write-Host "Lỗi: Container 'sqlserver-db' không chạy. Hãy chạy 'docker-compose up -d' trước." -ForegroundColor Red
    exit 1
}

# Đợi SQL Server sẵn sàng
Write-Host "Đang đợi SQL Server khởi động hoàn tất..."
$ready = $false
for ($i=0; $i -lt 30; $i++) {
    # Thử chạy một lệnh đơn giản
    $test = docker exec sqlserver-db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $saPassword -C -Q "SELECT 1" 2>$null
    if ($LASTEXITCODE -eq 0) {
        $ready = $true
        Write-Host "`nSQL Server đã sẵn sàng!" -ForegroundColor Green
        break
    }
    Write-Host "." -NoNewline
    Start-Sleep -Seconds 2
}

if (-not $ready) {
    Write-Host "`nLỗi: SQL Server không phản hồi sau 60 giây." -ForegroundColor Red
    exit 1
}

Write-Host "Bước 1: Chạy initDb.sql (Tạo Database và Bảng)..."
docker exec -it sqlserver-db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $saPassword -C -i /data/initDb.sql

Write-Host "Bước 2: Chạy seedDb.sql (Nạp dữ liệu mẫu)..."
docker exec -it sqlserver-db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $saPassword -C -i /data/seedDb.sql

Write-Host "--- Hoàn tất thiết lập Database! ---" -ForegroundColor Green
