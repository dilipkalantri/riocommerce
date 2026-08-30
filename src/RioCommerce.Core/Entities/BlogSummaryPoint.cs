namespace RioCommerce.Core.Entities;

/// <summary>
/// One bullet of a blog post's "In A Hurry" summary — the 30-second digest shown
/// between the article heading and the body.
///
/// Normalised child of <see cref="BlogPost"/> rather than five column pairs or a
/// JSON blob, so each point is independently queryable and the 1–5 ordering is a
/// real constraint: (BlogPostId, PointNumber) is unique and PointNumber is bounded
/// to 1–5 by a check constraint (migration 0027).
///
/// Whether the section renders is governed by <see cref="BlogPost.ShowSummary"/>,
/// not by the presence of rows — an editor can keep drafted points switched off.
/// </summary>
public class BlogSummaryPoint : BaseEntity
{
    public Guid BlogPostId { get; set; }

    /// <summary>1–5. Drives both the displayed badge and the sort order.</summary>
    public int PointNumber { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public BlogPost? BlogPost { get; set; }
}
