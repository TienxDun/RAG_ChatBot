$ErrorActionPreference = 'Stop'

function Stop-ProcessByName {
    param([string]$Name)

    $processes = Get-Process -Name $Name -ErrorAction SilentlyContinue
    if ($null -ne $processes) {
        $processes | Stop-Process -Force
    }
}

function Stop-PortProcess {
    param([int]$Port)

    $connections = Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue
    if ($null -eq $connections) {
        return
    }

    $pids = $connections | Select-Object -ExpandProperty OwningProcess -Unique
    foreach ($processId in $pids) {
        if ($processId -gt 0) {
            Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host 'Step 1: Stopping dotnet and node processes...'
Stop-ProcessByName -Name 'dotnet'
Stop-ProcessByName -Name 'node'

Write-Host 'Step 1: Releasing ports 5000, 3000 and 5173...'
Stop-PortProcess -Port 5000
Stop-PortProcess -Port 3000
Stop-PortProcess -Port 5173

Write-Host 'Step 2: Starting backend and frontend...'
$backendPath = Join-Path $PSScriptRoot '..\backend'
$frontendPath = Join-Path $PSScriptRoot '..\frontend'
$frontendNodeModules = Join-Path $frontendPath 'node_modules'

Start-Process -FilePath 'cmd.exe' -ArgumentList '/k', 'dotnet', 'run' -WorkingDirectory $backendPath

if (-not (Test-Path $frontendNodeModules)) {
    Write-Host 'Frontend: node_modules not found. Installing dependencies...'
    Push-Location $frontendPath
    try {
        npm install
    } finally {
        Pop-Location
    }
}

Start-Process -FilePath 'cmd.exe' -ArgumentList '/k', 'npm', 'run', 'dev' -WorkingDirectory $frontendPath

Write-Host 'Backend: http://localhost:5000'
Write-Host 'Frontend: http://localhost:3000'

Write-Host 'Step 3: Closing console.'
exit 0
