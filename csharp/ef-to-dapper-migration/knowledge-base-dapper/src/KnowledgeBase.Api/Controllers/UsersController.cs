using KnowledgeBase.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeBase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserRepository _users;

    public UsersController(IUserRepository users) => _users = users;

    // GET /api/users
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var results = await _users.GetAllAsync(ct);
        return Ok(results);
    }
}
