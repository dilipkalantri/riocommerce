using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using Microsoft.EntityFrameworkCore;
namespace RioCommerce.Infrastructure.Data.Seed;
public static class SeedData
{
    private static readonly DateTime SD = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);

    public static void Seed(ModelBuilder b)
    {
        // NOTE: Admin user is seeded in Program.cs using BCrypt at runtime

        b.Entity<Role>().HasData(
            new Role { Id = Guid.Parse("44444444-4444-4444-4444-444444444401"), Name = "super_admin", DisplayName = "Super Admin", IsSystem = true, IsActive = true, CreatedAt = SD, UpdatedAt = SD },
            new Role { Id = Guid.Parse("44444444-4444-4444-4444-444444444402"), Name = "admin", DisplayName = "Admin", IsSystem = true, IsActive = true, CreatedAt = SD, UpdatedAt = SD },
            new Role { Id = Guid.Parse("44444444-4444-4444-4444-444444444403"), Name = "student", DisplayName = "Student", IsSystem = true, IsActive = true, CreatedAt = SD, UpdatedAt = SD },
            new Role { Id = Guid.Parse("44444444-4444-4444-4444-444444444404"), Name = "faculty", DisplayName = "Faculty", IsSystem = true, IsActive = true, CreatedAt = SD, UpdatedAt = SD },
            new Role { Id = Guid.Parse("44444444-4444-4444-4444-444444444405"), Name = "franchise_admin", DisplayName = "Franchise Admin", IsSystem = true, IsActive = true, CreatedAt = SD, UpdatedAt = SD },
            new Role { Id = Guid.Parse("44444444-4444-4444-4444-444444444406"), Name = "operations", DisplayName = "Operations", IsSystem = true, IsActive = true, CreatedAt = SD, UpdatedAt = SD }
        );

        b.Entity<AppSetting>().HasData(
            new AppSetting { Key = "company_name", Value = "My Store", Category = "general", ValueType = "string", UpdatedAt = SD },
            new AppSetting { Key = "company_phone_1", Value = "", Category = "general", ValueType = "string", UpdatedAt = SD },
            new AppSetting { Key = "company_email", Value = "admin@example.com", Category = "general", ValueType = "string", UpdatedAt = SD },
            new AppSetting { Key = "company_address", Value = "", Category = "general", ValueType = "string", UpdatedAt = SD },
            new AppSetting { Key = "whatsapp_message", Value = "Hi! I'd like to enquire about your courses.", Category = "site", ValueType = "string", UpdatedAt = SD },
            new AppSetting { Key = "business_hours", Value = "Mon–Sat, 10:00 AM – 7:00 PM", Category = "site", ValueType = "string", UpdatedAt = SD }
        );
    }
}
