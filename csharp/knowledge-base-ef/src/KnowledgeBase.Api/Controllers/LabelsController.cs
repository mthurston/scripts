using KnowledgeBase.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeBase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LabelsController : ControllerBase
{
    private readonly ILabelRepository _labels;

    public LabelsController(ILabelRepository labels) => _labels = labels;

    // GET /api/labels
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var results = await _labels.GetAllAsync(ct);
        return Ok(results);
    }
}
