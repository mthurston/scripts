using KnowledgeBase.Core.Domain;
using KnowledgeBase.Core.DTOs;
using KnowledgeBase.Core.Interfaces;
using KnowledgeBase.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Data.Repositories;

public class UserRepository : IUserRepository
{
    private readonly KnowledgeBaseDbContext _context;

    public UserRepository(KnowledgeBaseDbContext context) => _context = context;

    public async Task<IEnumerable<UserDto>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Users
            .Select(u => new UserDto { Id = u.Id, Name = u.Name, Username = u.Username })
            .ToListAsync(ct);
    }

    public async Task<User> UpsertAsync(int gitLabUserId, string name, string username, CancellationToken ct = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.GitLabUserId == gitLabUserId, ct);
        if (user is null)
        {
            user = new User { GitLabUserId = gitLabUserId, Name = name, Username = username };
            _context.Users.Add(user);
        }
        else
        {
            user.Name = name;
            user.Username = username;
        }
        await _context.SaveChangesAsync(ct);
        return user;
    }
}
