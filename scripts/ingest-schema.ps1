$ErrorActionPreference = 'Stop'

# 1. Load Environment Variables
$envFile = Join-Path $PSScriptRoot "..\.env"
$envData = @{}

if (Test-Path $envFile) {
    Get-Content $envFile | ForEach-Object {
        $line = $_.Trim()
        if ($line -match "^(?<name>[^#\s][^=]*)=(?<value>.*)$") {
            $name = $Matches.name.Trim()
            $value = $Matches.value.Trim()
            $envData[$name] = $value
        }
    }
}

$apiKey = $envData["VERTEX_API_KEY"]
$projectId = $envData["VERTEX_PROJECT_ID"]
$region = $envData["VERTEX_REGION"]
$embedModel = $envData["VERTEX_EMBED_MODEL"]
$expressMode = $envData["VERTEX_EXPRESS_MODE"]

if (-not $apiKey -or -not $embedModel) {
    Write-Host "Error: VERTEX_API_KEY or VERTEX_EMBED_MODEL not found in .env" -ForegroundColor Red
    exit 1
}

$qdrantUrl = "http://localhost:6333"
$collectionName = "db_schema"

# 2. Check/Create Qdrant Collection
Write-Host "Checking Qdrant collection '$collectionName'..." -ForegroundColor Cyan
try {
    # Delete if exists to reset dimensions
    Invoke-RestMethod -Uri "$qdrantUrl/collections/$collectionName" -Method Delete > $null
    Write-Host "Old collection deleted."
} catch {}

Write-Host "Creating collection with 3072 dimensions..."
$createBody = @{
    vectors = @{
        size = 3072 # Gemini Embedding default size
        distance = "Cosine"
    }
} | ConvertTo-Json
Invoke-RestMethod -Uri "$qdrantUrl/collections/$collectionName" -Method Put -Body $createBody -ContentType "application/json"

# 3. Read Schema Description
$schemaPath = Join-Path $PSScriptRoot "..\data\schema-description.json"
$schemaData = Get-Content $schemaPath -Raw | ConvertFrom-Json

Write-Host "Starting ingestion of $($schemaData.Count) tables..." -ForegroundColor Cyan

# 4. Ingest each table
$points = @()
$id = 1

foreach ($table in $schemaData) {
    $columnsText = ($table.columns | ForEach-Object { "$($_.name): $($_.description)" }) -join "; "
    $fullText = "Table: $($table.table). Description: $($table.description). Columns: $columnsText"
    
    Write-Host "Embedding table: $($table.table)..."
    
    # Call Gemini Embedding API
    $hostName = if ($region -eq "global") { "aiplatform.googleapis.com" } else { "$region-aiplatform.googleapis.com" }
    
    # Correct URL construction
    if ($expressMode -eq "true") {
        $embedUrl = "https://aiplatform.googleapis.com/v1/publishers/google/models/$($embedModel):predict?key=$apiKey"
    } else {
        $embedUrl = "https://$hostName/v1/projects/$projectId/locations/$region/publishers/google/models/$($embedModel):predict?key=$apiKey"
    }

    $embedBody = @{
        instances = @(
            @{
                content = $fullText
                task_type = "RETRIEVAL_DOCUMENT"
            }
        )
    } | ConvertTo-Json

    # Write-Host "URL: $embedUrl" # Debug
    $response = Invoke-RestMethod -Uri $embedUrl -Method Post -Body $embedBody -ContentType "application/json"
    $vector = $response.predictions[0].embeddings.values

    # Add to points
    $points += @{
        id = $id
        vector = $vector
        payload = @{
            table = $table.table
            description = $table.description
            full_text = $fullText
        }
    }
    $id++
}

# 5. Upsert to Qdrant
Write-Host "Upserting points to Qdrant..." -ForegroundColor Cyan
$upsertBody = @{
    points = $points
} 

# Use -Compress to keep size down, and ensure it's a string
$jsonString = $upsertBody | ConvertTo-Json -Depth 10 -Compress

# Use PUT for upsert as per Qdrant docs
Invoke-RestMethod -Uri "$qdrantUrl/collections/$collectionName/points?wait=true" -Method Put -Body $jsonString -ContentType "application/json; charset=utf-8"

Write-Host "Ingestion complete!" -ForegroundColor Green
