using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using AgriDirect.Models;
using AgriDirect.Services;

namespace AgriDirect.Data
{
    public static class DbInitializer
    {
        public static async Task InitializeAsync(AppDbContext context, IPasswordHasher passwordHasher)
        {
            await context.Database.EnsureCreatedAsync();

            try
            {
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE Products ADD COLUMN AdditionalImageUrls TEXT NULL;");
            }
            catch
            {
                // Column already exists
            }

            if (context.Users.Any())
            {
                return; // DB has already been seeded
            }

            // 1. Seed Users
            var adminUser = new User
            {
                FullName = "Administrator",
                Email = "admin@agridirect.com",
                PasswordHash = passwordHasher.HashPassword("Admin@123"),
                PhoneNumber = "+91 98765 43210",
                Address = "AgriDirect Tech Park, Sector 44, Bengaluru, KA",
                Role = "Admin",
                ProfilePictureUrl = "/images/admin-avatar.svg",
                Status = "Active",
                CreatedAt = DateTime.UtcNow.AddMonths(-3)
            };

            var farmerRajesh = new User
            {
                FullName = "Rajesh Sharma",
                Email = "farmer@agridirect.com",
                PasswordHash = passwordHasher.HashPassword("Farmer@123"),
                PhoneNumber = "+91 94123 45678",
                Address = "Village Khed, Pune Rural, Maharashtra",
                Role = "Farmer",
                FarmName = "Green Valley Organic Farm",
                FarmLocation = "Pune, Maharashtra",
                ProfilePictureUrl = "/images/farmer-rajesh.svg",
                Status = "Active",
                AverageRating = 4.9,
                TotalRatingsCount = 18,
                CreatedAt = DateTime.UtcNow.AddMonths(-2)
            };

            var farmerAnita = new User
            {
                FullName = "Anita Patel",
                Email = "anita@agridirect.com",
                PasswordHash = passwordHasher.HashPassword("Farmer@123"),
                PhoneNumber = "+91 98220 11223",
                Address = "Ratnagiri Coastal Farms, Maharashtra",
                Role = "Farmer",
                FarmName = "Sunburst Agro Orchards",
                FarmLocation = "Ratnagiri, Maharashtra",
                ProfilePictureUrl = "/images/farmer-anita.svg",
                Status = "Active",
                AverageRating = 4.8,
                TotalRatingsCount = 12,
                CreatedAt = DateTime.UtcNow.AddMonths(-2)
            };

            var buyerRahul = new User
            {
                FullName = "Rahul Verma",
                Email = "buyer@agridirect.com",
                PasswordHash = passwordHasher.HashPassword("Buyer@123"),
                PhoneNumber = "+91 99887 76655",
                Address = "Flat 402, Sunshine Apartments, Kothrud, Pune, MH",
                Role = "Consumer",
                ProfilePictureUrl = "/images/buyer-rahul.svg",
                Status = "Active",
                CreatedAt = DateTime.UtcNow.AddMonths(-1)
            };

            var buyerPriya = new User
            {
                FullName = "Priya Desai",
                Email = "priya@agridirect.com",
                PasswordHash = passwordHasher.HashPassword("Buyer@123"),
                PhoneNumber = "+91 97654 32109",
                Address = "Villa 12, Green Glen Layout, Bellandur, Bengaluru, KA",
                Role = "Consumer",
                ProfilePictureUrl = "/images/buyer-priya.svg",
                Status = "Active",
                CreatedAt = DateTime.UtcNow.AddMonths(-1)
            };

            context.Users.AddRange(adminUser, farmerRajesh, farmerAnita, buyerRahul, buyerPriya);
            await context.SaveChangesAsync();

            // 2. Seed Products
            var products = new List<Product>
            {
                new Product
                {
                    FarmerId = farmerRajesh.Id,
                    Name = "Organic Basmati Rice",
                    Category = "Grains",
                    Price = 60.00m,
                    QuantityInStock = 450.00m,
                    Unit = "kg",
                    HarvestDate = DateTime.UtcNow.AddDays(-10).Date,
                    Description = "Naturally cultivated aromatic long-grain Basmati rice, chemical-free.",
                    ImageUrl = "",
                    Status = "Approved",
                    CreatedAt = DateTime.UtcNow.AddDays(-20)
                },
                new Product
                {
                    FarmerId = farmerRajesh.Id,
                    Name = "Fresh Farm Tomatoes",
                    Category = "Vegetables",
                    Price = 35.00m,
                    QuantityInStock = 200.00m,
                    Unit = "kg",
                    HarvestDate = DateTime.UtcNow.AddDays(-2).Date,
                    Description = "Vine-ripened, juicy red farm tomatoes picked this week.",
                    ImageUrl = "",
                    Status = "Approved",
                    CreatedAt = DateTime.UtcNow.AddDays(-5)
                },
                new Product
                {
                    FarmerId = farmerAnita.Id,
                    Name = "Farm Fresh Potatoes",
                    Category = "Vegetables",
                    Price = 25.00m,
                    QuantityInStock = 300.00m,
                    Unit = "kg",
                    HarvestDate = DateTime.UtcNow.AddDays(-5).Date,
                    Description = "Fresh organic potatoes directly harvested from the field.",
                    ImageUrl = "",
                    Status = "Approved",
                    CreatedAt = DateTime.UtcNow.AddDays(-7)
                },
                new Product
                {
                    FarmerId = farmerAnita.Id,
                    Name = "Himalayan Red Apples",
                    Category = "Fruits",
                    Price = 120.00m,
                    QuantityInStock = 150.00m,
                    Unit = "kg",
                    HarvestDate = DateTime.UtcNow.AddDays(-8).Date,
                    Description = "Crisp, sweet orchard apples harvested fresh.",
                    ImageUrl = "",
                    Status = "Approved",
                    CreatedAt = DateTime.UtcNow.AddDays(-8)
                }
            };

            context.Products.AddRange(products);
            await context.SaveChangesAsync();
        }
    }
}
