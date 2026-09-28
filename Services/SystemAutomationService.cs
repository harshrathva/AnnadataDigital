using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using AgriDirect.Data;
using AgriDirect.Models;

namespace AgriDirect.Services
{
    public interface ISystemAutomationService
    {
        Task SendNotificationAsync(int userId, string title, string message, string type, string? targetUrl = null);
        Task UpdateFarmerRatingAsync(int farmerId);
        Task ProcessPaymentStatusAsync(int orderId, string status);
    }

    public class SystemAutomationService : ISystemAutomationService
    {
        private readonly AppDbContext _context;

        public SystemAutomationService(AppDbContext context)
        {
            _context = context;
        }

        public async Task SendNotificationAsync(int userId, string title, string message, string type, string? targetUrl = null)
        {
            var notification = new Notification
            {
                UserId = userId,
                Title = title,
                Message = message,
                Type = type,
                IsRead = false,
                TargetUrl = targetUrl,
                CreatedAt = DateTime.UtcNow
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateFarmerRatingAsync(int farmerId)
        {
            var farmer = await _context.Users.FindAsync(farmerId);
            if (farmer == null) return;

            var reviews = await _context.Reviews.Where(r => r.FarmerId == farmerId).ToListAsync();
            if (reviews.Count > 0)
            {
                farmer.TotalRatingsCount = reviews.Count;
                farmer.AverageRating = Math.Round(reviews.Average(r => r.Rating), 1);
            }
            else
            {
                farmer.TotalRatingsCount = 0;
                farmer.AverageRating = 5.0;
            }

            await _context.SaveChangesAsync();
        }

        public async Task ProcessPaymentStatusAsync(int orderId, string status)
        {
            var order = await _context.Orders
                .Include(o => o.Product)
                .Include(o => o.Farmer)
                .Include(o => o.Buyer)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return;

            order.PaymentStatus = status;
            order.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Notify buyer
            await SendNotificationAsync(
                order.BuyerId,
                $"Payment {status}",
                $"Payment for order #{order.OrderNumber} is now marked as {status}.",
                "Payment",
                $"/Order/Details/{order.Id}"
            );

            // Notify farmer
            await SendNotificationAsync(
                order.FarmerId,
                $"Order Payment Update",
                $"Payment of ₹{order.TotalAmount} for order #{order.OrderNumber} is {status}.",
                "Payment",
                $"/Order/FarmerOrders"
            );
        }
    }
}
