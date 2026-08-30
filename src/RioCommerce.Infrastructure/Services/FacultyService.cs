using RioCommerce.Core.DTOs.Faculty;
using RioCommerce.Core.DTOs.Products;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Core.Interfaces.Repositories;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class FacultyService : IFacultyService
{
    private readonly RioCommerceDbContext _db;
    private readonly IProductRepository _products;

    public FacultyService(RioCommerceDbContext db, IProductRepository products)
    {
        _db = db;
        _products = products;
    }

    public async Task<List<FacultyCard>> ListAsync(bool homeOnly = false)
    {
        var q = _db.Faculty.Where(f => f.IsActive);
        if (homeOnly) q = q.Where(f => f.ShowOnHomePage);
        var faculty = await q.OrderBy(f => f.DisplayOrder).ToListAsync();

        // Per-faculty course count + rating aggregate from their active primary products.
        var rows = await _db.Products
            .Where(p => p.Status == ProductStatus.Active && p.PrimaryFacultyId != null)
            .Select(p => new { Fid = p.PrimaryFacultyId!.Value, p.AvgRating, p.RatingCount })
            .ToListAsync();

        var byFaculty = rows.GroupBy(x => x.Fid).ToDictionary(
            g => g.Key,
            g => (Count: g.Count(), RatingCount: g.Sum(x => x.RatingCount), RatingSum: g.Sum(x => x.AvgRating * x.RatingCount)));

        var cards = new List<FacultyCard>();
        foreach (var f in faculty)
        {
            byFaculty.TryGetValue(f.Id, out var s);
            // Public /faculty listing: hide anyone with no active course. Homepage carousel: the admin
            // has explicitly ticked them, so feature them even if they currently teach 0 active products.
            if (!homeOnly && s.Count == 0) continue;
            var avg = s.RatingCount == 0 ? 0 : Math.Round(s.RatingSum / s.RatingCount, 2);
            cards.Add(new FacultyCard(f.Id, f.ShortCode, f.DisplayName, f.Designation, f.Qualifications,
                f.PhotoUrl, f.Subjects, f.YearsOfExperience, f.StudentsTaught,
                f.ShortDescription, f.Bio,
                s.Count, avg, s.RatingCount));
        }
        return cards;
    }

    public async Task<FacultyProfile?> GetByCodeAsync(string code)
    {
        var f = await _db.Faculty.FirstOrDefaultAsync(x => x.IsActive && x.ShortCode.ToLower() == code.ToLower());
        if (f == null) return null;

        var page = await _products.GetFilteredAsync(new ProductFilterRequest { FacultyId = f.Id, Page = 1, PageSize = 100 });
        var courses = page.Items;

        var ratingCount = courses.Sum(c => c.RatingCount);
        var avg = ratingCount == 0 ? 0 : Math.Round(courses.Sum(c => c.AvgRating * c.RatingCount) / ratingCount, 2);

        return new FacultyProfile(f.Id, f.ShortCode, f.DisplayName, f.Designation, f.Qualifications,
            f.ShortDescription, f.Bio,
            f.PhotoUrl, f.YoutubeUrl, f.WhatsappNumber, f.CallNumber, f.Subjects,
            f.YearsOfExperience, f.StudentsTaught, f.HoursOfTeaching, f.StudentSatisfaction, f.AirHoldersNote,
            courses.Count, avg, ratingCount, courses);
    }
}
