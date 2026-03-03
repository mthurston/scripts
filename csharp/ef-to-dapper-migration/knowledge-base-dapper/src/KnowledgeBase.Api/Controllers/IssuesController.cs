using KnowledgeBase.Core.DTOs;
using KnowledgeBase.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeBase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IssuesController : ControllerBase
{
    private readonly IIssueRepository _issues;
    private readonly ICommentRepository _comments;

    public IssuesController(IIssueRepository issues, ICommentRepository comments)
    {
        _issues = issues;
        _comments = comments;
    }

    // GET /api/issues?label=bug&state=opened&author=jsmith
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? label,
        [FromQuery] string? state,
        [FromQuery] string? author,
        CancellationToken ct)
    {
        var results = await _issues.GetAllAsync(label, state, author, ct);
        return Ok(results);
    }

    // GET /api/issues/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var issue = await _issues.GetByIdAsync(id, ct);
        if (issue is null) return NotFound();
        return Ok(issue);
    }

    // PATCH /api/issues/{id}
    [HttpPatch("{id:int}")]
    public async Task<IActionResult> Patch(int id, [FromBody] PatchIssueRequest request, CancellationToken ct)
    {
        await _issues.PatchAsync(id, request, ct);
        return NoContent();
    }

    // POST /api/issues/{id}/comments
    [HttpPost("{id:int}/comments")]
    public async Task<IActionResult> AddComment(
        int id,
        [FromBody] CreateCommentRequest request,
        CancellationToken ct)
    {
        var comment = await _comments.AddAsync(id, request, ct);
        return CreatedAtAction(nameof(GetById), new { id }, comment);
    }
}
