using KnowledgeBase.Core.Domain;
using KnowledgeBase.Core.DTOs;

namespace KnowledgeBase.Core.Interfaces;

public interface IUserRepository
{
    Task<IEnumerable<UserDto>> GetAllAsync(CancellationToken ct = default);
    Task<User> UpsertAsync(int gitLabUserId, string name, string username, CancellationToken ct = default);
}
