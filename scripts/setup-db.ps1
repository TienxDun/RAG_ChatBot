$ErrorActionPreference = 'Stop'

# Duong dan toi file .env
$envFile = Join-Path $PSScriptRoot "..\.env"
$saPassword = ""

# Doc mat khau tu file .env
if (Test-Path $envFile) {
    $content = Get-Content $envFile
    foreach ($line in $content) {
        if ($line -match "^MSSQL_SA_PASSWORD=(.*)$") {
            $saPassword = $matches[1].Trim()
            break
        }
    }
}

# Neu khong tim thay trong .env, dung mac dinh
if (-not $saPassword) {
    $saPassword = "YourStrong@Password123"
}

Write-Host "--- Khoi tao Database SQL Server ---" -ForegroundColor Cyan

# Kiem tra container co dang chay khong
$containerStatus = docker inspect -f '{{.State.Running}}' sqlserver-db 2>$null
if ($containerStatus -ne "true") {
    Write-Host "Loi: Container 'sqlserver-db' khong chay. Hay chay 'docker-compose up -d' truoc." -ForegroundColor Red
    exit 1
}

# Doi SQL Server san sang
Write-Host "Dang doi SQL Server khoi dong hoan tat..."
$ready = $false
for ($i=0; $i -lt 30; $i++) {
    # Thu chay mot lenh don gian
    $test = docker exec sqlserver-db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $saPassword -C -Q "SELECT 1" 2>$null
    if ($LASTEXITCODE -eq 0) {
        $ready = $true
        Write-Host "`nSQL Server da san sang!" -ForegroundColor Green
        break
    }
    Write-Host "." -NoNewline
    Start-Sleep -Seconds 2
}

if (-not $ready) {
    Write-Host "`nLoi: SQL Server khong phan hoi sau 60 giay." -ForegroundColor Red
    exit 1
}

Write-Host "Buoc 1: Chay initDb.sql (Tao Database va Bang)..."
docker exec -i sqlserver-db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $saPassword -C -i /data/initDb.sql

Write-Host "Buoc 2: Chay seedDb.sql (Nap du lieu mau)..."
docker exec -i sqlserver-db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $saPassword -C -i /data/seedDb.sql

Write-Host "--- Hoan tat thiet lap Database! ---" -ForegroundColor Green
