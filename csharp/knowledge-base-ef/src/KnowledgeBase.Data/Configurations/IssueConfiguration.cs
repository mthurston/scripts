using KnowledgeBase.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeBase.Data.Configurations;

public class IssueConfiguration : IEntityTypeConfiguration<Issue>
{
    public void Configure(EntityTypeBuilder<Issue> builder)
    {
        builder.ToTable("Issues");
        builder.HasKey(i => i.Id);
        builder.HasIndex(i => i.GitLabId).IsUnique();

        builder.Property(i => i.Title).IsRequired().HasMaxLength(500);
        builder.Property(i => i.State).IsRequired().HasMaxLength(50);
        builder.Property(i => i.WebUrl).IsRequired().HasMaxLength(1000);
        builder.Property(i => i.KbCategory).HasMaxLength(200);
        builder.Property(i => i.KbNotes).HasMaxLength(4000);

        builder.HasOne(i => i.Author)
               .WithMany()
               .HasForeignKey(i => i.AuthorId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
