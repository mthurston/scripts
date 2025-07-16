# RabbitMQ Connectivity Test Script Usage

## Overview
The `dev_rabbitmq_test.ps1` script is a comprehensive RabbitMQ connectivity verification tool that tests all major components of your RabbitMQ server.

## Default Configuration
The script uses these default values:
- **Host**: `localhost`
- **Management Port**: `15672`
- **AMQP Port**: `5672`
- **Username**: `guest`
- **Password**: `guest`
- **Virtual Host**: `/` (default virtual host)

## Basic Usage Examples

### 1. Test with Default Settings
```powershell
# Test local RabbitMQ with default guest/guest credentials
.\dev_rabbitmq_test.ps1
```

### 2. Test with Custom Credentials
```powershell
# Test with your custom username and password
.\dev_rabbitmq_test.ps1 -Username "myuser" -Password "mypassword"
```

### 3. Test Custom Virtual Host
```powershell
# Test a specific virtual host
.\dev_rabbitmq_test.ps1 -Username "myuser" -Password "mypassword" -VirtualHost "production"
```

### 4. Test Remote RabbitMQ Server
```powershell
# Test a remote RabbitMQ server
.\dev_rabbitmq_test.ps1 -RabbitMQHost "rabbitmq.example.com" -Username "admin" -Password "secret123"
```

### 5. Test with Custom Ports
```powershell
# Test with non-standard ports
.\dev_rabbitmq_test.ps1 -ManagementPort 8080 -AMQPPort 5673
```

### 6. Verbose Output for Debugging
```powershell
# Get detailed output for troubleshooting
.\dev_rabbitmq_test.ps1 -Username "myuser" -Password "mypassword" -Verbose
```

### 7. Complete Custom Configuration
```powershell
# Test with all custom parameters
.\dev_rabbitmq_test.ps1 -RabbitMQHost "rabbitmq.internal.com" `
                        -ManagementPort 8080 `
                        -AMQPPort 5673 `
                        -Username "testuser" `
                        -Password "testpass123" `
                        -VirtualHost "test-environment" `
                        -Verbose
```

### 8. Skip AMQP Protocol Testing
```powershell
# Skip AMQP testing if RabbitMQ .NET Client is not available or not needed
.\dev_rabbitmq_test.ps1 -SkipAMQPTest
```

### 9. Enable AMQP Protocol Testing
To enable full AMQP protocol testing, install the RabbitMQ .NET Client:
```powershell
# Install RabbitMQ .NET Client via NuGet
Install-Package RabbitMQ.Client
```

Or manually place the `RabbitMQ.Client.dll` in the script directory.

## What the Script Tests

The script performs 11 comprehensive tests:

1. **Management API Connection** - Basic connectivity to RabbitMQ Management API
2. **Virtual Host Access** - Verifies virtual host exists and is accessible
3. **User Exists** - Checks if the specified user exists in RabbitMQ
4. **User Permissions** - Verifies user has proper permissions for the virtual host
5. **Create Test Exchange** - Creates a temporary direct exchange (auto-deleted)
6. **Create Test Queue** - Creates a temporary queue (auto-deleted)
7. **Bind Queue to Exchange** - Binds queue to exchange with routing key
8. **Publish Test Message** - Sends a test message via Management API
9. **Queue Status Check** - Verifies queue statistics (message count, consumers)
10. **Consume Test Message** - Retrieves and acknowledges the message via Management API
11. **AMQP Port Connectivity** - Tests raw TCP connectivity to AMQP port
12. **AMQP Protocol Test** - Direct AMQP connection with queue operations (if RabbitMQ .NET Client is available)

## Setting Up RabbitMQ for Testing

### Default Setup (guest user)
The RabbitMQ default `guest` user only works from `localhost`. If you're testing locally with a fresh RabbitMQ install, these commands will work:

```powershell
# Basic test with defaults
.\dev_rabbitmq_test.ps1
```

### Custom User Setup
To create a custom user for testing:

```bash
# Create a new user
rabbitmqctl add_user testuser testpass123

# Set user as administrator
rabbitmqctl set_user_tags testuser administrator

# Grant permissions on default virtual host
rabbitmqctl set_permissions -p / testuser ".*" ".*" ".*"

# Create and grant permissions on custom virtual host
rabbitmqctl add_vhost test-environment
rabbitmqctl set_permissions -p test-environment testuser ".*" ".*" ".*"
```

Then test with:
```powershell
.\dev_rabbitmq_test.ps1 -Username "testuser" -Password "testpass123" -VirtualHost "test-environment"
```

## Expected Output

### Success Example
```
🐰 RabbitMQ Connectivity Test
Host: localhost | Management Port: 15672 | AMQP Port: 5672
Virtual Host: / | User: guest
AMQP Testing: Enabled
------------------------------------------------------------
✓ PASS - Management API Connection
✓ PASS - Virtual Host Access
✓ PASS - User Exists
✓ PASS - User Permissions
✓ PASS - Create Test Exchange
✓ PASS - Create Test Queue
✓ PASS - Bind Queue to Exchange
✓ PASS - Publish Test Message
✓ PASS - Queue Status Check
✓ PASS - Consume Test Message
✓ PASS - AMQP Port Connectivity
✓ PASS - AMQP Protocol Test
✓ PASS - AMQP Queue Declaration
✓ PASS - AMQP Message Publish
✓ PASS - AMQP Message Consume
------------------------------------------------------------
🧹 Cleaning up test resources...
✓ Deleted test queue
✓ Test exchange already cleaned up (auto-deleted)
------------------------------------------------------------
🎉 All tests passed! RabbitMQ server is functioning properly.
```

### Failure Example
```
✗ FAIL - Management API Connection
  Failed to connect: Unable to connect to the remote server
```

## Exit Codes
- **0**: All tests passed
- **1**: One or more tests failed

## Common Issues and Solutions

### Issue: Guest user access denied
**Solution**: The guest user is restricted to localhost connections only. Either:
1. Test from localhost, or
2. Create a custom user with proper permissions

### Issue: Virtual host not found
**Solution**: Verify the virtual host exists:
```powershell
# List available virtual hosts
rabbitmqctl list_vhosts
```

### Issue: User permissions denied
**Solution**: Check user permissions:
```powershell
# List user permissions
rabbitmqctl list_permissions -p your-vhost
```

### Issue: Management API not enabled
**Solution**: Enable the management plugin:
```bash
rabbitmq-plugins enable rabbitmq_management
```

### Issue: RabbitMQ .NET Client not available
**Solution**: Install the RabbitMQ .NET Client for full AMQP protocol testing:
```powershell
# Via NuGet Package Manager
Install-Package RabbitMQ.Client

# Or manually download and place RabbitMQ.Client.dll in script directory
```

### Issue: AMQP tests failing
**Solution**: Check if:
1. AMQP port (5672) is accessible
2. User has proper permissions for AMQP operations
3. RabbitMQ .NET Client is properly installed

## Integration with CI/CD

The script returns appropriate exit codes for automation:

```powershell
# In a CI/CD pipeline
.\dev_rabbitmq_test.ps1 -Username "ci-user" -Password "ci-password"
if ($LASTEXITCODE -ne 0) {
    Write-Error "RabbitMQ connectivity test failed"
    exit 1
}
Write-Host "RabbitMQ is ready for deployment"
```
