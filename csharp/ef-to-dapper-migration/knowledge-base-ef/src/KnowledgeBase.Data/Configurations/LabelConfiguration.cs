using KnowledgeBase.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeBase.Data.Configurations;

public class LabelConfiguration : IEntityTypeConfiguration<Label>
{
    public void Configure(EntityTypeBuilder<Label> builder)
    {
        builder.ToTable("Labels");
        builder.HasKey(l => l.Id);
        builder.HasIndex(l => l.Name).IsUnique();
        builder.Property(l => l.Name).IsRequired().HasMaxLength(100);
        builder.Property(l => l.Color).IsRequired().HasMaxLength(20);
        builder.Property(l => l.Description).HasMaxLength(500);
    }
}
