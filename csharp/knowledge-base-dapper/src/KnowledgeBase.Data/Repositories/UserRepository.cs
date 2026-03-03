using Dapper;
using KnowledgeBase.Core.Domain;
using KnowledgeBase.Core.DTOs;
using KnowledgeBase.Core.Interfaces;
using KnowledgeBase.Data.SqlQueries;

namespace KnowledgeBase.Data.Repositories;

public class UserRepository : IUserRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public UserRepository(IDbConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<IEnumerable<UserDto>> GetAllAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<dynamic>(UserQueries.GetAll);
        return rows.Select(r => new UserDto
        {
            Id = (int)r.Id,
            Name = (string)r.Name,
            Username = (string)r.Username
        });
    }

    public async Task<User> UpsertAsync(int gitLabUserId, string name, string username, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleAsync<dynamic>(
            UserQueries.Upsert,
            new { GitLabUserId = gitLabUserId, Name = name, Username = username });

        return new User
        {
            Id = (int)row.Id,
            GitLabUserId = (int)row.GitLabUserId,
            Name = (string)row.Name,
            Username = (string)row.Username
        };
    }
}
