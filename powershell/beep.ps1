# powershell script that will beep every 30 seconds until user provides input
$beepInterval = 30 # seconds

Write-Host "Beeping every $beepInterval seconds. Press any key to stop..." -ForegroundColor Yellow

$elapsed = $beepInterval
while ($true) {
    # Check if a key is available
    if ([Console]::KeyAvailable) {
        $key = [Console]::ReadKey($true)
        Write-Host "`nStopping beep script. Key pressed: $($key.Key)" -ForegroundColor Green
        break
    }
    
    # Check if it's time to beep
    if ($elapsed -ge $beepInterval) {
        Write-Host "Beep!" -ForegroundColor Cyan
        [console]::beep(1000, 3500) # Beep at 1000 Hz for 3500 milliseconds
        $elapsed = 0
    }
    
    Start-Sleep -Seconds 1
    $elapsed++
}