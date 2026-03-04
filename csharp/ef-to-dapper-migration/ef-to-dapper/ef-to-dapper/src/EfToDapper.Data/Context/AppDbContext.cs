using EfToDapper.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace EfToDapper.Data.Context;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<PostTag> PostTags => Set<PostTag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // PostTag composite primary key
        modelBuilder.Entity<PostTag>()
            .HasKey(pt => new { pt.PostId, pt.TagId });

        modelBuilder.Entity<PostTag>()
            .HasOne(pt => pt.Post)
            .WithMany(p => p.PostTags)
            .HasForeignKey(pt => pt.PostId);

        modelBuilder.Entity<PostTag>()
            .HasOne(pt => pt.Tag)
            .WithMany(t => t.PostTags)
            .HasForeignKey(pt => pt.TagId);

        // Post.Body is a large nvarchar(max) column — simulates a wide table to illustrate over-fetching.
        // EF Core maps string → nvarchar(max) by default on SQL Server, so no explicit mapping needed.

        // -----------------------------------------------------------------------
        // SCENARIO 4 — Key Lookup / Missing Index
        // -----------------------------------------------------------------------
        // ANTIPATTERN: The Email column has NO index.
        // SQL Server must perform a Clustered Index Scan on every "find by email"
        // query, then follow a Key Lookup pointer back to the main row for each match.
        // You can see this in SSMS: Actual Execution Plan → "Key Lookup" operator.
        //
        // FIX: Uncomment one of the blocks below, then restart the app.
        // EnsureCreated will re-create the schema with the index in place.
        // (Or drop + recreate the EfToDapperDemo database to pick up schema changes.)
        //
        // Option A — simple non-clustered index (eliminates the scan):
        //   modelBuilder.Entity<Author>()
        //       .HasIndex(a => a.Email)
        //       .HasDatabaseName("IX_Authors_Email");
        //
        // Option B — covering index (also eliminates the Key Lookup entirely):
        //   SQL Server stores Name + Bio in the index leaf pages, so the engine
        //   never needs to follow the pointer back to the clustered index row.
        //   modelBuilder.Entity<Author>()
        //       .HasIndex(a => a.Email)
        //       .IncludeProperties(a => new { a.Name, a.Bio })
        //       .HasDatabaseName("IX_Authors_Email_Covering");
        // -----------------------------------------------------------------------
    }
}
