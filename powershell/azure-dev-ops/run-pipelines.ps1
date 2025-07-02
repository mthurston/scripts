# run various Azure DevOps pipelines for prosperity
# This script triggers multiple Azure DevOps pipelines in sequence and waits for their completion.
# It retrieves a Personal Access Token (PAT) from Windows Credential Manager or prompts the user for it.
# The required scope for the PAT is 'Build (read and execute)'.

# Configuration
$organization = "your-org-name"
$project = "your-project-name"
$patName = "your-pat-name" # Optional: Name for the PAT in Credential Manager

# Pipeline definitions (replace with your actual pipeline IDs)
$pipelines = @(
    @{ Name = "Pipeline 1"; Id = 123; Ref = "refs/heads/main" }, 
    @{ Name = "Pipeline 2"; Id = 456; Ref = "refs/heads/develop" }, 
    @{ Name = "Pipeline 3"; Id = 789; Ref = "refs/heads/feature-branch" } 
)

# Function to get stored credential
function Get-SavedCredential {
    param([string]$Target)
    
    try {
        # Try using CredentialManager module first - install if not available
        if (-not (Get-Module -ListAvailable -Name CredentialManager)) {
            Write-Host "CredentialManager module not found. Installing..." -ForegroundColor Yellow
            try {
                Install-Module -Name CredentialManager -Force -Scope CurrentUser -ErrorAction Stop
                Write-Host "CredentialManager module installed successfully." -ForegroundColor Green
            }
            catch {
                Write-Warning "Failed to install CredentialManager module: $($_.Exception.Message)"
            }
        }
        
        if (Get-Module -ListAvailable -Name CredentialManager) {
            Import-Module CredentialManager -ErrorAction Stop
            # Use the module's Get-StoredCredential function
            $storedCred = Get-StoredCredential -Target $Target -Type Generic -ErrorAction Stop
            if ($storedCred) {
                return $storedCred
            }
        }
        
        # If module approach fails, throw to trigger manual entry
        throw "Automatic retrieval failed"
    }
    catch {
        # Final fallback: Check if credential exists
        $cmdOutput = cmdkey /list:$Target 2>$null
        if ($cmdOutput -match "Target: $Target") {
            throw "Credential exists but cannot be retrieved automatically. Please enter manually."
        }
        else {
            throw "Credential not found in Windows Credential Manager"
        }
    }
}

# Retrieve PAT from Windows Credential Manager or prompt user
try {
    # Prompt for organization and project if not set
    if ($organization -eq "your-org-name" -or [string]::IsNullOrEmpty($organization)) {
        $organization = Read-Host "Enter your Azure DevOps organization name"
    }

    if ($project -eq "your-project-name" -or [string]::IsNullOrEmpty($project)) {
        $project = Read-Host "Enter your Azure DevOps project name"
    }    

    # default $patName to "AzureDevOps-PAT" if not provided
    if ($patName -eq "your-pat-name" -or [string]::IsNullOrEmpty($patName)) {
        $tempPatName = "AzureDevOps-PAT-$organization"
        # check to see if the credential exists
        $cmdOutput = cmdkey /list:$tempPatName 2>$null
        if ($cmdOutput -match "Target: $tempPatName") {
            $patName = $tempPatName
        }
        else {
            # If not found, prompt for a new PAT name
            $patName = Read-Host "Enter a name for your Personal Access Token (PAT) in Credential Manager (default: AzureDevOps-PAT-$organization)"
            if ([string]::IsNullOrEmpty($patName)) {
                $patName = "AzureDevOps-PAT-$organization"
            }
        }
    }    Write-Host "Attempting to retrieve PAT from Credential Manager using name: '$patName'" -ForegroundColor Cyan
    $credential = Get-SavedCredential -Target $patName
    $pat = $credential.GetNetworkCredential().Password
    Write-Host "Successfully retrieved PAT from Credential Manager" -ForegroundColor Green
}
catch {
    Write-Host "Error details: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "PAT not found in Credential Manager. Please provide your credentials." -ForegroundColor Yellow

    # Prompt for PAT securely
    $securePatString = Read-Host "Enter your Personal Access Token (PAT)" -AsSecureString
    $pat = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePatString))

    # Ask if user wants to save credentials
    $saveCredentials = Read-Host "Would you like to save these credentials to Windows Credential Manager? (y/n)"
    if ($saveCredentials -eq 'y' -or $saveCredentials -eq 'Y') {
        try {
            # Store in credential manager using the variable name
            Start-Process -FilePath "cmdkey" -ArgumentList "/generic:$patName", "/user:pat", "/pass:$pat" -Wait -WindowStyle Hidden
            Write-Host "Credentials saved to Windows Credential Manager" -ForegroundColor Green
        }
        catch {
            Write-Error "Failed to save credentials to Credential Manager: $($_.Exception.Message)"
        }
    }
}

# Create authentication header
$encodedPat = [System.Convert]::ToBase64String([System.Text.Encoding]::ASCII.GetBytes(":$pat"))
$headers = @{
    Authorization  = "Basic $encodedPat"
    "Content-Type" = "application/json"
}

# Function to trigger pipeline
function Start-Pipeline {
    param($PipelineId, $PipelineName, $BranchRef = "refs/heads/main")
    
    $uri = "https://dev.azure.com/$organization/$project/_apis/pipelines/$PipelineId/runs?api-version=7.0"
    $body = @{
        resources = @{
            repositories = @{
                self = @{
                    refName = $BranchRef
                }
            }
        }
    } | ConvertTo-Json -Depth 3
    
    try {
        Write-Host "Starting $PipelineName on branch $BranchRef..." -ForegroundColor Yellow
        $response = Invoke-RestMethod -Uri $uri -Method Post -Headers $headers -Body $body
        Write-Host "Successfully started $PipelineName (Run ID: $($response.id))" -ForegroundColor Green
        return $response.id
    }
    catch {
        Write-Error "Failed to start $PipelineName`: $($_.Exception.Message)"
        return $null
    }
}

# Function to check pipeline status
function Wait-PipelineCompletion {
    param($RunId, $PipelineName, $PipelineId)
    $uri = "https://dev.azure.com/$organization/$project/_apis/pipelines/$PipelineId/runs/$RunId`?api-version=7.0"
    
    try {
        do {
            Start-Sleep -Seconds 60
            $run = Invoke-RestMethod -Uri $uri -Headers $headers
            Write-Host "$PipelineName status: $($run.state)" -ForegroundColor Cyan
        } while ($run.state -eq "inProgress")
        
        if ($run.result -eq "succeeded") {
            Write-Host "$PipelineName completed successfully!" -ForegroundColor Green
            return $true
        }
        elseif ($run.result -eq "failed") {
            Write-Host "$PipelineName failed!" -ForegroundColor Red
            return $false
        }
        elseif ($run.result -eq "canceled") {
            Write-Host "$PipelineName was canceled!" -ForegroundColor Yellow
            return $false
        }
        else {
            Write-Host "$PipelineName completed with result: $($run.result)" -ForegroundColor Yellow
            return $false
        }
    }
    catch {
        Write-Error "Failed to check status for $PipelineName`: $($_.Exception.Message)"
        Write-Error "Run ID: $RunId, Pipeline ID: $PipelineId, Organization: $organization, Project: $project, URI: $uri"
        return $false
    }
}

# Execute pipelines in sequence
foreach ($pipeline in $pipelines) {
    Write-Host "Processing pipeline: $($pipeline.Name) (ID: $($pipeline.Id))" -ForegroundColor Cyan
    $runId = Start-Pipeline -PipelineId $pipeline.Id -PipelineName $pipeline.Name -BranchRef $pipeline.Ref
    
    if ($runId) {
        $success = Wait-PipelineCompletion -RunId $runId -PipelineName $pipeline.Name -PipelineId $pipeline.Id
        
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