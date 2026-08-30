using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace RioCommerce.Infrastructure.Data.Configurations;

public class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> b)
    {
        b.ToTable("approval_requests"); b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(300).IsRequired();
        b.Property(x => x.RelatedEntityType).HasMaxLength(60);
        b.Property(x => x.RequestedByName).HasMaxLength(200);
        b.Property(x => x.DecidedByName).HasMaxLength(200);
        b.Property(x => x.Amount).HasPrecision(12, 2);
        b.HasIndex(x => x.Status);
        b.HasIndex(x => new { x.RelatedEntityType, x.RelatedEntityId });
        b.HasMany(x => x.Steps).WithOne(s => s.ApprovalRequest).HasForeignKey(s => s.ApprovalRequestId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Comments).WithOne(c => c.ApprovalRequest).HasForeignKey(c => c.ApprovalRequestId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ApprovalStepConfiguration : IEntityTypeConfiguration<ApprovalStep>
{
    public void Configure(EntityTypeBuilder<ApprovalStep> b)
    {
        b.ToTable("approval_steps"); b.HasKey(x => x.Id);
        b.Property(x => x.ApproverRole).HasMaxLength(60);
        b.HasIndex(x => x.ApprovalRequestId);
    }
}

public class ApprovalCommentConfiguration : IEntityTypeConfiguration<ApprovalComment>
{
    public void Configure(EntityTypeBuilder<ApprovalComment> b)
    {
        b.ToTable("approval_comments"); b.HasKey(x => x.Id);
        b.Property(x => x.Body).HasMaxLength(2000).IsRequired();
        b.Property(x => x.AuthorName).HasMaxLength(200);
        b.HasIndex(x => x.ApprovalRequestId);
    }
}
