using Dapper;
using KnowledgeBase.Core.Domain;
using KnowledgeBase.Core.DTOs;
using KnowledgeBase.Core.Interfaces;
using KnowledgeBase.Data.SqlQueries;

namespace KnowledgeBase.Data.Repositories;

public class LabelRepository : ILabelRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public LabelRepository(IDbConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<IEnumerable<LabelDto>> GetAllAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<dynamic>(LabelQueries.GetAll);
        return rows.Select(r => new LabelDto
        {
            Id = (int)r.Id,
            Name = (string)r.Name,
            Color = (string)r.Color,
            Description = (string?)r.Description
        });
    }

    public async Task<Label> UpsertAsync(string name, string color, string? description, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleAsync<dynamic>(
            LabelQueries.Upsert,
            new { Name = name, Color = color, Description = description });

        return new Label
        {
            Id = (int)row.Id,
            Name = (string)row.Name,
            Color = (string)row.Color,
            Description = (string?)row.Description
        };
    }
}
