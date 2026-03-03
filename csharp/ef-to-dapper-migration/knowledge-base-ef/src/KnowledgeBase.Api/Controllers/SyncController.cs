using KnowledgeBase.Data.Services;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeBase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SyncController : ControllerBase
{
    private readonly SyncService _syncService;

    public SyncController(SyncService syncService) => _syncService = syncService;

    // POST /api/sync
    [HttpPost]
    public async Task<IActionResult> TriggerSync(CancellationToken ct)
    {
        var result = await _syncService.SyncAsync(ct);
        return Ok(result);
    }
}
