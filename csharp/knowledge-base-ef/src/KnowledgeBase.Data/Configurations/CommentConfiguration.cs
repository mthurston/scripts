using KnowledgeBase.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeBase.Data.Configurations;

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("Comments");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Body).IsRequired().HasMaxLength(4000);
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.UpdatedAt).IsRequired();

        // Self-referencing relationship for threaded comments.
        // Restrict prevents cascade loops when deleting a parent comment.
        builder.HasOne(c => c.ParentComment)
               .WithMany(c => c.Replies)
               .HasForeignKey(c => c.ParentCommentId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Issue)
               .WithMany(i => i.Comments)
               .HasForeignKey(c => c.IssueId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Author)
               .WithMany()
               .HasForeignKey(c => c.AuthorId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
