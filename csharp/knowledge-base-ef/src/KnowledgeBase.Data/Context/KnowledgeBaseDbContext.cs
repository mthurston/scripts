using KnowledgeBase.Core.Domain;
using KnowledgeBase.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Data.Context;

public class KnowledgeBaseDbContext : DbContext
{
    public KnowledgeBaseDbContext(DbContextOptions<KnowledgeBaseDbContext> options)
        : base(options) { }

    public DbSet<Issue> Issues => Set<Issue>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<IssueLabel> IssueLabels => Set<IssueLabel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KnowledgeBaseDbContext).Assembly);
    }
}
