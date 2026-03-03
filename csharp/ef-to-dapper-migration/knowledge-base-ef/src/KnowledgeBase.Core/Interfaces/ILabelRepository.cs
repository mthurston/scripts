using KnowledgeBase.Core.Domain;
using KnowledgeBase.Core.DTOs;

namespace KnowledgeBase.Core.Interfaces;

public interface ILabelRepository
{
    Task<IEnumerable<LabelDto>> GetAllAsync(CancellationToken ct = default);
    Task<Label> UpsertAsync(string name, string color, string? description, CancellationToken ct = default);
}
