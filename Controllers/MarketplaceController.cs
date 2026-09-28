using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AgriDirect.Data;
using AgriDirect.Models.ViewModels;

namespace AgriDirect.Controllers
{
    public class MarketplaceController : Controller
    {
        private readonly AppDbContext _context;

        public MarketplaceController(AppDbContext context)
        {
            _context = context;
        }

        // GET: /Marketplace or /
        [HttpGet]
        public async Task<IActionResult> Index(
            string? keyword,
            string? category,
            decimal? minPrice,
            decimal? maxPrice,
            string? location,
            bool onlyAvailable = false,
            string sortBy = "newest")
        {
            var query = _context.Products
                .Include(p => p.Farmer)
                .Include(p => p.Reviews)
                .Where(p => p.Status == "Approved" && p.Farmer != null && p.Farmer.Status == "Active");

            // Keyword Search (R.3.2)
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = keyword.Trim().ToLower();
                query = query.Where(p => 
                    p.Name.ToLower().Contains(kw) ||
                    p.Description.ToLower().Contains(kw) ||
                    p.Category.ToLower().Contains(kw) ||
                    (p.Farmer != null && (p.Farmer.FullName.ToLower().Contains(kw) || (p.Farmer.FarmName != null && p.Farmer.FarmName.ToLower().Contains(kw))))
                );
            }

            // Category Filter (R.3.3)
            if (!string.IsNullOrWhiteSpace(category) && category != "All")
            {
                query = query.Where(p => p.Category == category);
            }

            // Price Range Filter (R.3.3)
            if (minPrice.HasValue && minPrice.Value > 0)
            {
                query = query.Where(p => p.Price >= minPrice.Value);
            }
            if (maxPrice.HasValue && maxPrice.Value > 0)
            {
                query = query.Where(p => p.Price <= maxPrice.Value);
            }

            // Location Filter (R.3.3)
            if (!string.IsNullOrWhiteSpace(location) && location != "All")
            {
                query = query.Where(p => p.Farmer != null && (
                    (p.Farmer.FarmLocation != null && p.Farmer.FarmLocation.Contains(location)) ||
                    p.Farmer.Address.Contains(location)
                ));
            }

            // Availability Filter (R.3.3)
            if (onlyAvailable)
            {
                query = query.Where(p => p.QuantityInStock > 0);
            }

            // Sorting
            query = sortBy switch
            {
                "price_low" => query.OrderBy(p => p.Price),
                "price_high" => query.OrderByDescending(p => p.Price),
                "rating" => query.OrderByDescending(p => p.Farmer != null ? p.Farmer.AverageRating : 0),
                _ => query.OrderByDescending(p => p.CreatedAt) // newest
            };

            var products = await query.ToListAsync();

            // Distinct categories and locations for filter dropdowns
            var categories = await _context.Products
                .Where(p => p.Status == "Approved")
                .Select(p => p.Category)
                .Distinct()
                .ToListAsync();

            var locations = await _context.Users
                .Where(u => u.Role == "Farmer" && u.FarmLocation != null)
                .Select(u => u.FarmLocation!)
                .Distinct()
                .ToListAsync();

            var viewModel = new MarketplaceFilterViewModel
            {
                Keyword = keyword,
                Category = category,
                MinPrice = minPrice,
                MaxPrice = maxPrice,
                Location = location,
                OnlyAvailable = onlyAvailable,
                SortBy = sortBy,
                Products = products,
                AvailableCategories = categories,
                AvailableLocations = locations
            };

            return View(viewModel);
        }
    }
}
