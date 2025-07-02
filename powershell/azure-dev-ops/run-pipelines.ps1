# run various Azure DevOps pipelines for prosperity

# Configuration
$organization = "your-org-name"
$project = "your-project-name"
$patName = "your-pat-name" # Optional: Name for the PAT in Credential Manager

# Retrieve PAT from Windows Credential Manager or prompt user
try {
    # default $patName to "AzureDevOps-PAT" if not provided
    if ($patName -eq "your-pat-name" -or [string]::IsNullOrEmpty($patName)) {
        $patName = "AzureDevOps-PAT"
    }
        
    # Prompt for organization and project if not set
    if ($organization -eq "your-org-name" -or [string]::IsNullOrEmpty($organization)) {
        $organization = Read-Host "Enter your Azure DevOps organization name"
    }

    if ($project -eq "your-project-name" -or [string]::IsNullOrEmpty($project)) {
        $project = Read-Host "Enter your Azure DevOps project name"
    }    

    $credential = Get-StoredCredential -Target $patName
    $pat = $credential.GetNetworkCredential().Password
    Write-Host "Successfully retrieved PAT from Credential Manager" -ForegroundColor Green
}
catch {
    Write-Host "PAT not found in Credential Manager. Please provide your credentials." -ForegroundColor Yellow

    # Prompt for PAT securely
    $securePatString = Read-Host "Enter your Personal Access Token (PAT)" -AsSecureString
    $pat = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePatString))
    
    # Ask if user wants to save credentials
    $saveCredentials = Read-Host "Would you like to save these credentials to Windows Credential Manager? (y/n)"
    if ($saveCredentials -eq 'y' -or $saveCredentials -eq 'Y') {
        try {
            # Store in credential manager
            Start-Process -FilePath "cmdkey" -ArgumentList "/generic:AzureDevOps-PAT", "/user:pat", "/pass:$pat" -Wait -WindowStyle Hidden
            Write-Host "Credentials saved to Windows Credential Manager" -ForegroundColor Green
        }
        catch {
            Write-Warning "Failed to save credentials to Credential Manager, but continuing with session..."
        }
    }
}

# Pipeline definitions (replace with your actual pipeline IDs)
$pipelines = @(
    @{ Name = "Pipeline 1"; Id = 123; Ref = "refs/heads/main" }, 
    @{ Name = "Pipeline 2"; Id = 456; Ref = "refs/heads/develop" }, 
    @{ Name = "Pipeline 3"; Id = 789; Ref = "refs/heads/feature-branch" } 
)

# Create authentication header
$encodedPat = [System.Convert]::ToBase64String([System.Text.Encoding]::ASCII.GetBytes(":$pat"))
$headers = @{
    Authorization  = "Basic $encodedPat"
    "Content-Type" = "application/json"
}

# Function to get stored credential
function Get-StoredCredential {
    param([string]$Target)
    
    Add-Type -AssemblyName System.Web
    $credential = New-Object System.Management.Automation.PSCredential("pat", (ConvertTo-SecureString -String (cmdkey /list:$Target 2>$null | Select-String "Password:" | ForEach-Object { $_.ToString().Split(":")[1].Trim() }) -AsPlainText -Force))
    return $credential
}

# ...existing code...

# Function to trigger pipeline
function Start-Pipeline {
    param($PipelineId, $PipelineName)
    
    $uri = "https://dev.azure.com/$organization/$project/_apis/pipelines/$PipelineId/runs?api-version=7.0"
    $body = @{
        resources = @{
            repositories = @{
                self = @{
                    refName = "refs/heads/main" # or your target branch
                }
            }
        }
    } | ConvertTo-Json -Depth 3
    
    try {
        Write-Host "Starting $PipelineName..." -ForegroundColor Yellow
        $response = Invoke-RestMethod -Uri $uri -Method Post -Headers $headers -Body $body
        return $response.id
    }
    catch {
        Write-Error "Failed to start $PipelineName`: $($_.Exception.Message)"
        return $null
    }
}

# Function to check pipeline status
function Wait-PipelineCompletion {
    param($RunId, $PipelineName)
    
    $uri = "https://dev.azure.com/$organization/$project/_apis/pipelines/runs/$RunId?api-version=7.0"
    
    do {
        Start-Sleep -Seconds 30
        $run = Invoke-RestMethod -Uri $uri -Headers $headers
        Write-Host "$PipelineName status: $($run.state)" -ForegroundColor Cyan
    } while ($run.state -eq "inProgress")
    
    if ($run.result -eq "succeeded") {
        Write-Host "$PipelineName completed successfully!" -ForegroundColor Green
        return $true
    }
    else {
        Write-Host "$PipelineName failed with result: $($run.result)" -ForegroundColor Red
        return $false
    }
}

# Execute pipelines in sequence
foreach ($pipeline in $pipelines) {
    $runId = Start-Pipeline -PipelineId $pipeline.Id -PipelineName $pipeline.Name
    
    if ($runId) {
        $success = Wait-PipelineCompletion -RunId $runId -PipelineName $pipeline.Name
        
        if (-not $success) {
            Write-Host "Pipeline sequence stopped due to failure in $($pipeline.Name)" -ForegroundColor Red
            exit 1
        }
    }
    else {
        Write-Host "Failed to start $($pipeline.Name). Stopping sequence." -ForegroundColor Red
        exit 1
    }
}

Write-Host "All pipelines completed successfully!" -ForegroundColor Green