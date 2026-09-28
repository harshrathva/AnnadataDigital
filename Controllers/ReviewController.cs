using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AgriDirect.Data;
using AgriDirect.Models;
using AgriDirect.Models.ViewModels;
using AgriDirect.Services;

namespace AgriDirect.Controllers
{
    [Authorize(Roles = "Consumer")]
    public class ReviewController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ISystemAutomationService _automationService;

        public ReviewController(AppDbContext context, ISystemAutomationService automationService)
        {
            _context = context;
            _automationService = automationService;
        }

        // GET: /Review/Create/{orderId}
        [HttpGet]
        public async Task<IActionResult> Create(int orderId)
        {
            var buyerId = GetCurrentUserId();
            var order = await _context.Orders
                .Include(o => o.Product)
                .Include(o => o.Farmer)
                .Include(o => o.Reviews)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerId == buyerId);

            if (order == null) return NotFound();

            if (order.OrderStatus != "Delivered")
            {
                TempData["Error"] = "You can only submit a review after the order has been delivered.";
                return RedirectToAction("Details", "Order", new { id = orderId });
            }

            if (order.Reviews.Any())
            {
                TempData["Info"] = "You have already reviewed this order. Thank you!";
                return RedirectToAction("Details", "Order", new { id = orderId });
            }

            var model = new ReviewViewModel
            {
                OrderId = order.Id,
                ProductId = order.ProductId,
                ProductName = order.Product?.Name,
                FarmerName = order.Farmer?.FullName,
                Rating = 5
            };

            return View(model);
        }

        // POST: /Review/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ReviewViewModel model)
        {
            var buyerId = GetCurrentUserId();
            var order = await _context.Orders
                .Include(o => o.Product)
                .Include(o => o.Farmer)
                .Include(o => o.Reviews)
                .FirstOrDefaultAsync(o => o.Id == model.OrderId && o.BuyerId == buyerId);

            if (order == null) return NotFound();

            if (order.Reviews.Any())
            {
                TempData["Info"] = "You have already reviewed this order.";
                return RedirectToAction("Details", "Order", new { id = model.OrderId });
            }

            if (!ModelState.IsValid)
            {
                model.ProductName = order.Product?.Name;
                model.FarmerName = order.Farmer?.FullName;
                return View(model);
            }

            var review = new Review
            {
                OrderId = order.Id,
                ProductId = order.ProductId,
                FarmerId = order.FarmerId,
                BuyerId = buyerId,
                Rating = model.Rating,
                Comment = model.Comment.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            _context.Reviews.Add(review);
            await _context.SaveChangesAsync();

            // Recalculate farmer's average rating (R.6.1)
            await _automationService.UpdateFarmerRatingAsync(order.FarmerId);

            // Notify farmer
            await _automationService.SendNotificationAsync(
                order.FarmerId,
                "🌟 New Customer Review!",
                $"A buyer gave you a {review.Rating}-star rating for order #{order.OrderNumber}: \"{review.Comment}\"",
                "System",
                $"/Product/Details/{order.ProductId}"
            );

            TempData["Success"] = "Thank you! Your rating and review have been submitted.";
            return RedirectToAction("Details", "Order", new { id = order.Id });
        }

        private int GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier);
            return claim != null ? int.Parse(claim.Value) : 0;
        }
    }
}
