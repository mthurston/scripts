# RabbitMQ Connectivity Test Script
# This script verifies RabbitMQ server connectivity using the Management API and AMQP protocol

param (
    [string] $RabbitMQHost = "localhost",
    [int] $ManagementPort = 15672,
    [int] $AMQPPort = 5672,
    [string] $Username = "guest",
    [string] $Password = "guest",
    [string] $VirtualHost = "/",
    [switch] $Verbose,
    [switch] $SkipAMQPTest = $false
)

# Configuration
$testQueueName = "test-connectivity-queue"
$testExchangeName = "test-connectivity-exchange"
$testRoutingKey = "test.routing.key"
$testQueueNameAMQP = "test-amqp-queue"

# Try to load RabbitMQ .NET client
$rabbitMQClientLoaded = $false
try {
    # Check if RabbitMQ.Client is available
    if (Get-Module -ListAvailable -Name RabbitMQ.Client -ErrorAction SilentlyContinue) {
        Import-Module RabbitMQ.Client
        $rabbitMQClientLoaded = $true
    } else {
        # Try to load the assembly directly (if installed via NuGet or manually)
        Add-Type -Path "RabbitMQ.Client.dll" -ErrorAction SilentlyContinue
        $rabbitMQClientLoaded = $true
    }
} catch {
    Write-Host "⚠ RabbitMQ .NET Client not available. AMQP tests will be skipped." -ForegroundColor Yellow
    Write-Host "  To enable AMQP testing, install the RabbitMQ .NET Client:" -ForegroundColor Gray
    Write-Host "  Install-Package RabbitMQ.Client" -ForegroundColor Gray
    $rabbitMQClientLoaded = $false
}

# Create authorization header
$credentials = [System.Convert]::ToBase64String([System.Text.Encoding]::ASCII.GetBytes("${Username}:${Password}"))
$headers = @{
    "Authorization" = "Basic $credentials"
    "Content-Type" = "application/json"
}

# Base URLs
$protocol = if ($UseHTTP) { "http" } else { "https" }
$baseUrl = "${protocol}://${RabbitMQHost}:${ManagementPort}/api"
$vhostEncoded = [System.Uri]::EscapeDataString($VirtualHost)

function Write-TestResult {
    param($TestName, $Success, $Message = "", $Details = $null)
    
    $status = if ($Success) { "✓ PASS" } else { "✗ FAIL" }
    $color = if ($Success) { "Green" } else { "Red" }
    
    Write-Host "$status - $TestName" -ForegroundColor $color
    if ($Message) { Write-Host "  $Message" -ForegroundColor Gray }
    if ($Details -and $Verbose) { 
        Write-Host "  Details: $($Details | ConvertTo-Json -Compress)" -ForegroundColor DarkGray 
    }
}

function Test-AMQPConnection {
    param($ConnectionFactory, $TestName)
    
    try {
        $connection = $ConnectionFactory.CreateConnection()
        $channel = $connection.CreateModel()
        
        Write-TestResult $TestName $true "AMQP connection established successfully"
        
        # Test basic channel operations
        $channel.QueueDeclare($testQueueNameAMQP, $false, $false, $true, $null)
        Write-TestResult "AMQP Queue Declaration" $true "Queue '$testQueueNameAMQP' declared via AMQP"
        
        # Publish a message via AMQP
        $messageBody = [System.Text.Encoding]::UTF8.GetBytes("Test AMQP message from PowerShell at $(Get-Date)")
        $channel.BasicPublish("", $testQueueNameAMQP, $null, $messageBody)
        Write-TestResult "AMQP Message Publish" $true "Message published via AMQP protocol"
        
        # Consume the message via AMQP
        $result = $channel.BasicGet($testQueueNameAMQP, $true)
        if ($result) {
            $consumedMessage = [System.Text.Encoding]::UTF8.GetString($result.Body.ToArray())
            Write-TestResult "AMQP Message Consume" $true "Message consumed via AMQP: '$consumedMessage'"
        } else {
            Write-TestResult "AMQP Message Consume" $false "No message available via AMQP"
        }
        
        # Clean up AMQP resources
        $channel.QueueDelete($testQueueNameAMQP)
        $channel.Close()
        $connection.Close()
        
        return $true
    }
    catch {
        Write-TestResult $TestName $false "AMQP connection failed: $($_.Exception.Message)"
        return $false
    }
}

function Test-RabbitMQConnection {
    Write-Host "🐰 RabbitMQ Connectivity Test" -ForegroundColor Cyan
    Write-Host "Host: $RabbitMQHost | Management Port: $ManagementPort | AMQP Port: $AMQPPort" -ForegroundColor Gray
    Write-Host "Virtual Host: $VirtualHost | User: $Username" -ForegroundColor Gray
    if ($rabbitMQClientLoaded -and -not $SkipAMQPTest) {
        Write-Host "AMQP Testing: Enabled" -ForegroundColor Green
    } else {
        Write-Host "AMQP Testing: Disabled" -ForegroundColor Yellow
    }
    Write-Host ("-" * 60) -ForegroundColor Gray
    
    $allTestsPassed = $true
    
    # Test 1: Management API Connectivity
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/overview" -Headers $headers -TimeoutSec 10
        Write-TestResult "Management API Connection" $true "Connected to RabbitMQ $($response.rabbitmq_version)"
    }
    catch {
        Write-TestResult "Management API Connection" $false "Failed to connect: $($_.Exception.Message)"
        $allTestsPassed = $false
        return $false
    }
    
    # Test 2: Virtual Host Access
    try {
        $vhosts = Invoke-RestMethod -Uri "$baseUrl/vhosts" -Headers $headers
        $targetVhost = $vhosts | Where-Object { $_.name -eq $VirtualHost }
        if ($targetVhost) {
            Write-TestResult "Virtual Host Access" $true "Virtual host '$VirtualHost' accessible"
        } else {
            Write-TestResult "Virtual Host Access" $false "Virtual host '$VirtualHost' not found"
            $allTestsPassed = $false
        }
    }
    catch {
        Write-TestResult "Virtual Host Access" $false "Failed to access virtual hosts: $($_.Exception.Message)"
        $allTestsPassed = $false
    }
# Test 3: User Permissions
    try {
        # First, check if the user exists
        $user = Invoke-RestMethod -Uri "$baseUrl/users/$Username" -Headers $headers
        Write-TestResult "User Exists" $true "User '$Username' found with tags: $($user.tags)"
        
        # Check user permissions for the virtual host
        $permissions = Invoke-RestMethod -Uri "$baseUrl/permissions/$vhostEncoded/$Username" -Headers $headers
        if ($permissions) {
            Write-TestResult "User Permissions" $true "User has permissions: configure='$($permissions.configure)', write='$($permissions.write)', read='$($permissions.read)'"
        } else {
            Write-TestResult "User Permissions" $false "No permissions found for virtual host '$VirtualHost'"
            $allTestsPassed = $false
        }
    }
    catch {
        # Try alternative API endpoint for permissions
        try {
            $allPermissions = Invoke-RestMethod -Uri "$baseUrl/permissions" -Headers $headers
            $userPermission = $allPermissions | Where-Object { $_.user -eq $Username -and $_.vhost -eq $VirtualHost }
            if ($userPermission) {
                Write-TestResult "User Permissions" $true "User has permissions: configure='$($userPermission.configure)', write='$($userPermission.write)', read='$($userPermission.read)'"
            } else {
                Write-TestResult "User Permissions" $false "No permissions found for user '$Username' on virtual host '$VirtualHost'"
                $allTestsPassed = $false
            }
        }
        catch {
            Write-TestResult "User Permissions" $false "Failed to check permissions: $($_.Exception.Message)"
            $allTestsPassed = $false
        }
    }
    
    # Test 4: Create Test Exchange
    try {
        $exchangeBody = @{
            type = "direct"
            durable = $false
            auto_delete = $true
            arguments = @{}
        } | ConvertTo-Json
        
        Invoke-RestMethod -Uri "$baseUrl/exchanges/$vhostEncoded/$testExchangeName" -Method PUT -Headers $headers -Body $exchangeBody
        Write-TestResult "Create Test Exchange" $true "Exchange '$testExchangeName' created successfully"
    }
    catch {
        Write-TestResult "Create Test Exchange" $false "Failed to create exchange: $($_.Exception.Message)"
        $allTestsPassed = $false
    }
    
    # Test 5: Create Test Queue
    try {
        $queueBody = @{
            durable = $false
            auto_delete = $true
            arguments = @{}
        } | ConvertTo-Json
        
        Invoke-RestMethod -Uri "$baseUrl/queues/$vhostEncoded/$testQueueName" -Method PUT -Headers $headers -Body $queueBody
        Write-TestResult "Create Test Queue" $true "Queue '$testQueueName' created successfully"
    }
    catch {
        Write-TestResult "Create Test Queue" $false "Failed to create queue: $($_.Exception.Message)"
        $allTestsPassed = $false
    }
    
    # Test 6: Bind Queue to Exchange
    try {
        $bindingBody = @{
            routing_key = $testRoutingKey
            arguments = @{}
        } | ConvertTo-Json
        
        Invoke-RestMethod -Uri "$baseUrl/bindings/$vhostEncoded/e/$testExchangeName/q/$testQueueName" -Method POST -Headers $headers -Body $bindingBody
        Write-TestResult "Bind Queue to Exchange" $true "Queue bound to exchange with routing key '$testRoutingKey'"
    }
    catch {
        Write-TestResult "Bind Queue to Exchange" $false "Failed to bind queue: $($_.Exception.Message)"
        $allTestsPassed = $false
    }
    
    # Test 7: Publish Test Message
    try {
        $messageBody = @{
            properties = @{}
            routing_key = $testRoutingKey
            payload = "Test message from PowerShell at $(Get-Date)"
            payload_encoding = "string"
        } | ConvertTo-Json
        
        $publishResult = Invoke-RestMethod -Uri "$baseUrl/exchanges/$vhostEncoded/$testExchangeName/publish" -Method POST -Headers $headers -Body $messageBody
        if ($publishResult.routed) {
            Write-TestResult "Publish Test Message" $true "Message published and routed successfully"
        } else {
            Write-TestResult "Publish Test Message" $false "Message published but not routed"
        }
    }
    catch {
        Write-TestResult "Publish Test Message" $false "Failed to publish message: $($_.Exception.Message)"
        $allTestsPassed = $false
    }
    
    # Test 8: Check Queue Status
    try {
        $queueInfo = Invoke-RestMethod -Uri "$baseUrl/queues/$vhostEncoded/$testQueueName" -Headers $headers
        Write-TestResult "Queue Status Check" $true "Queue has $($queueInfo.messages) messages, $($queueInfo.consumers) consumers"
    }
    catch {
        Write-TestResult "Queue Status Check" $false "Failed to get queue status: $($_.Exception.Message)"
        $allTestsPassed = $false
    }
    
    # Test 9: Consume Test Message
    try {
        $consumeBody = @{
            count = 1
            ackmode = "ack_requeue_false"
            encoding = "auto"
        } | ConvertTo-Json
        
        $messages = Invoke-RestMethod -Uri "$baseUrl/queues/$vhostEncoded/$testQueueName/get" -Method POST -Headers $headers -Body $consumeBody
        if ($messages -and $messages.Count -gt 0) {
            Write-TestResult "Consume Test Message" $true "Successfully consumed message: '$($messages[0].payload)'"
        } else {
            Write-TestResult "Consume Test Message" $false "No messages available to consume"
        }
    }
    catch {
        Write-TestResult "Consume Test Message" $false "Failed to consume message: $($_.Exception.Message)"
        $allTestsPassed = $false
    }
    
    # Test 10: AMQP Port Connectivity
    try {
        $tcpClient = New-Object System.Net.Sockets.TcpClient
        $tcpClient.Connect($RabbitMQHost, $AMQPPort)
        $tcpClient.Close()
        Write-TestResult "AMQP Port Connectivity" $true "Port $AMQPPort is accessible"
    }
    catch {
        Write-TestResult "AMQP Port Connectivity" $false "Port $AMQPPort is not accessible: $($_.Exception.Message)"
        $allTestsPassed = $false
    }
    
    # Test 11: Direct AMQP Connection and Message Operations
    if ($rabbitMQClientLoaded -and -not $SkipAMQPTest) {
        try {
            $factory = New-Object RabbitMQ.Client.ConnectionFactory
            $factory.HostName = $RabbitMQHost
            $factory.Port = $AMQPPort
            $factory.UserName = $Username
            $factory.Password = $Password
            $factory.VirtualHost = $VirtualHost
            
            $amqpTestPassed = Test-AMQPConnection -ConnectionFactory $factory -TestName "AMQP Protocol Test"
            if (-not $amqpTestPassed) {
                $allTestsPassed = $false
            }
        }
        catch {
            Write-TestResult "AMQP Protocol Test" $false "Failed to create AMQP connection factory: $($_.Exception.Message)"
            $allTestsPassed = $false
        }
    } else {
        Write-TestResult "AMQP Protocol Test" $false "Skipped - RabbitMQ .NET Client not available or disabled"
    }

    # Cleanup
    Write-Host ("-" * 60) -ForegroundColor Gray
    Write-Host "🧹 Cleaning up test resources..." -ForegroundColor Yellow
    
    try {
        # Delete test queue (this also removes bindings)
        Invoke-RestMethod -Uri "$baseUrl/queues/$vhostEncoded/$testQueueName" -Method DELETE -Headers $headers
        Write-Host "✓ Deleted test queue" -ForegroundColor Green
    }
    catch {
        Write-Host "⚠ Failed to delete test queue: $($_.Exception.Message)" -ForegroundColor Yellow
    }
    
    try {
        # Directly attempt to delete the test exchange
        Invoke-RestMethod -Uri "$baseUrl/exchanges/$vhostEncoded/$testExchangeName" -Method DELETE -Headers $headers
        Write-Host "✓ Deleted test exchange" -ForegroundColor Green
    }
    catch {
        if ($_.Exception.Response.StatusCode -eq 404) {
            Write-Host "✓ Test exchange already cleaned up (auto-deleted)" -ForegroundColor Green
        } else {
            Write-Host "⚠ Failed to delete test exchange: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
    
    # Final Result
    Write-Host ("-" * 60) -ForegroundColor Gray
    if ($allTestsPassed) {
        Write-Host "🎉 All tests passed! RabbitMQ server is functioning properly." -ForegroundColor Green
    } else {
        Write-Host "❌ Some tests failed. Please check your RabbitMQ configuration." -ForegroundColor Red
    }
    
    return $allTestsPassed
}

# Run the tests
$result = Test-RabbitMQConnection

# Exit with appropriate code
if ($result) {
    exit 0
} else {
    exit 1
}
