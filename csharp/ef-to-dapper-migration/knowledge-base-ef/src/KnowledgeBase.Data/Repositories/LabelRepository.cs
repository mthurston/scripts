using KnowledgeBase.Core.Domain;
using KnowledgeBase.Core.DTOs;
using KnowledgeBase.Core.Interfaces;
using KnowledgeBase.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Data.Repositories;

public class LabelRepository : ILabelRepository
{
    private readonly KnowledgeBaseDbContext _context;

    public LabelRepository(KnowledgeBaseDbContext context) => _context = context;

    public async Task<IEnumerable<LabelDto>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Labels
            .Select(l => new LabelDto
            {
                Id = l.Id,
                Name = l.Name,
                Color = l.Color,
                Description = l.Description
            })
            .ToListAsync(ct);
    }

    public async Task<Label> UpsertAsync(string name, string color, string? description, CancellationToken ct = default)
    {
        var label = await _context.Labels.FirstOrDefaultAsync(l => l.Name == name, ct);
        if (label is null)
        {
            label = new Label { Name = name, Color = color, Description = description };
            _context.Labels.Add(label);
        }
        else
        {
            label.Color = color;
            label.Description = description;
        }
        await _context.SaveChangesAsync(ct);
        return label;
    }
}
