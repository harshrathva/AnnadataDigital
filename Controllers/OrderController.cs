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
    [Authorize]
    public class OrderController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ISystemAutomationService _automationService;

        public OrderController(AppDbContext context, ISystemAutomationService automationService)
        {
            _context = context;
            _automationService = automationService;
        }

        // GET: /Order/Checkout/{productId} (R.5.1)
        [Authorize(Roles = "Consumer")]
        [HttpGet]
        public async Task<IActionResult> Checkout(int productId, decimal quantity = 1)
        {
            var product = await _context.Products
                .Include(p => p.Farmer)
                .FirstOrDefaultAsync(p => p.Id == productId && p.Status == "Approved");

            if (product == null) return NotFound();

            var buyerId = GetCurrentUserId();
            var buyer = await _context.Users.FindAsync(buyerId);

            var model = new OrderCheckoutViewModel
            {
                ProductId = product.Id,
                Product = product,
                Quantity = quantity > 0 ? quantity : 1,
                UnitPrice = product.Price,
                DeliveryAddress = buyer?.Address ?? "",
                PhoneNumber = buyer?.PhoneNumber ?? "",
                PaymentMethod = "CashOnDelivery"
            };

            return View(model);
        }

        // POST: /Order/PlaceOrder (R.5.1)
        [Authorize(Roles = "Consumer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PlaceOrder(OrderCheckoutViewModel model)
        {
            var product = await _context.Products
                .Include(p => p.Farmer)
                .FirstOrDefaultAsync(p => p.Id == model.ProductId && p.Status == "Approved");

            if (product == null) return NotFound();

            if (model.Quantity <= 0)
            {
                ModelState.AddModelError("Quantity", "Order quantity must be greater than zero.");
            }

            if (model.Quantity > product.QuantityInStock)
            {
                ModelState.AddModelError("Quantity", $"Requested quantity ({model.Quantity} {product.Unit}) exceeds available inventory ({product.QuantityInStock} {product.Unit}).");
            }

            if (!ModelState.IsValid)
            {
                model.Product = product;
                model.UnitPrice = product.Price;
                return View("Checkout", model);
            }

            var buyerId = GetCurrentUserId();
            var orderNumber = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpper()}";
            var totalAmount = model.Quantity * product.Price;

            var order = new Order
            {
                OrderNumber = orderNumber,
                BuyerId = buyerId,
                FarmerId = product.FarmerId,
                ProductId = product.Id,
                Quantity = model.Quantity,
                UnitPrice = product.Price,
                TotalAmount = totalAmount,
                OrderStatus = "Pending", // R.5.1 initial status Pending
                PaymentStatus = (model.PaymentMethod == "CashOnDelivery") ? "Pending" : "Paid",
                PaymentMethod = model.PaymentMethod,
                DeliveryAddress = model.DeliveryAddress.Trim(),
                TrackingNotes = string.IsNullOrWhiteSpace(model.Notes) ? "Order placed by buyer." : model.Notes.Trim(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Deduct product stock
            product.QuantityInStock -= model.Quantity;

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // Send automated notifications (R.7.4)
            await _automationService.SendNotificationAsync(
                product.FarmerId,
                "🛒 New Farm Order Received!",
                $"Order #{order.OrderNumber} placed for {order.Quantity} {product.Unit} of {product.Name}.",
                "Order",
                $"/Order/Details/{order.Id}"
            );

            await _automationService.SendNotificationAsync(
                buyerId,
                "✅ Order Confirmed",
                $"Your order #{order.OrderNumber} has been received and is Pending farmer acceptance.",
                "Order",
                $"/Order/Details/{order.Id}"
            );

            TempData["Success"] = $"Order #{order.OrderNumber} placed successfully!";
            return RedirectToAction("Details", new { id = order.Id });
        }

        // GET: /Order/History (R.5.3 - Buyer Order History)
        [HttpGet]
        public async Task<IActionResult> History()
        {
            var userId = GetCurrentUserId();
            var orders = await _context.Orders
                .Include(o => o.Product)
                .Include(o => o.Farmer)
                .Include(o => o.Reviews)
                .Where(o => o.BuyerId == userId)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            return View(orders);
        }

        // GET: /Order/FarmerOrders (Farmer Order Fulfillment Dashboard)
        [Authorize(Roles = "Farmer")]
        [HttpGet]
        public async Task<IActionResult> FarmerOrders()
        {
            var farmerId = GetCurrentUserId();
            var orders = await _context.Orders
                .Include(o => o.Product)
                .Include(o => o.Buyer)
                .Where(o => o.FarmerId == farmerId)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            return View(orders);
        }

        // GET: /Order/Details/{id} (R.5.2 - Order Tracking View for Buyer & Farmer)
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var userId = GetCurrentUserId();
            var order = await _context.Orders
                .Include(o => o.Product)
                .Include(o => o.Farmer)
                .Include(o => o.Buyer)
                .Include(o => o.Reviews)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound();

            // Authorization check
            bool isAdmin = User.IsInRole("Admin");
            if (!isAdmin && order.BuyerId != userId && order.FarmerId != userId)
            {
                return Forbid();
            }

            return View(order);
        }

        // POST: /Order/UpdateStatus (R.5.2 - Farmer updates order tracking status)
        [Authorize(Roles = "Farmer,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(OrderStatusUpdateViewModel model)
        {
            var order = await _context.Orders
                .Include(o => o.Product)
                .Include(o => o.Buyer)
                .FirstOrDefaultAsync(o => o.Id == model.OrderId);

            if (order == null) return NotFound();

            var currentUserId = GetCurrentUserId();
            if (!User.IsInRole("Admin") && order.FarmerId != currentUserId)
            {
                return Forbid();
            }

            var validStatuses = new[] { "Pending", "Accepted", "Packed", "Shipped", "Delivered", "Cancelled" };
            if (!validStatuses.Contains(model.NewStatus))
            {
                TempData["Error"] = "Invalid status specified.";
                return RedirectToAction(nameof(Details), new { id = model.OrderId });
            }

            order.OrderStatus = model.NewStatus;
            order.UpdatedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(model.Notes))
            {
                order.TrackingNotes = model.Notes.Trim();
            }

            // If marked delivered and payment was COD, automatically mark paid
            if (model.NewStatus == "Delivered" && order.PaymentStatus == "Pending")
            {
                order.PaymentStatus = "Paid";
            }

            await _context.SaveChangesAsync();

            // Send notification to buyer (R.5.2 & R.7.4)
            await _automationService.SendNotificationAsync(
                order.BuyerId,
                $"📦 Order Status: {model.NewStatus}",
                $"Your order #{order.OrderNumber} status has changed to '{model.NewStatus}'. {(string.IsNullOrWhiteSpace(model.Notes) ? "" : "Note: " + model.Notes)}",
                "Order",
                $"/Order/Details/{order.Id}"
            );

            TempData["Success"] = $"Order #{order.OrderNumber} status updated to {model.NewStatus}.";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }

        // POST: /Order/CancelOrder (R.5.2 - Buyer or Admin cancels pending order)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelOrder(int orderId)
        {
            var order = await _context.Orders
                .Include(o => o.Product)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return NotFound();

            var currentUserId = GetCurrentUserId();
            bool isAdmin = User.IsInRole("Admin");

            if (!isAdmin && order.BuyerId != currentUserId && order.FarmerId != currentUserId)
            {
                return Forbid();
            }

            if (order.OrderStatus == "Delivered" || order.OrderStatus == "Cancelled")
            {
                TempData["Error"] = $"Cannot cancel order in '{order.OrderStatus}' status.";
                return RedirectToAction(nameof(Details), new { id = orderId });
            }

            order.OrderStatus = "Cancelled";
            order.UpdatedAt = DateTime.UtcNow;
            order.TrackingNotes = "Order was cancelled.";

            // Return stock back to inventory
            if (order.Product != null)
            {
                order.Product.QuantityInStock += order.Quantity;
            }

            await _context.SaveChangesAsync();

            // Send automated notifications (R.7.4)
            await _automationService.SendNotificationAsync(
                order.FarmerId,
                "Order Cancelled",
                $"Order #{order.OrderNumber} has been cancelled. Inventory was restored.",
                "Order",
                $"/Order/Details/{order.Id}"
            );

            await _automationService.SendNotificationAsync(
                order.BuyerId,
                "Order Cancelled",
                $"Your order #{order.OrderNumber} has been successfully cancelled.",
                "Order",
                $"/Order/Details/{order.Id}"
            );

            TempData["Success"] = $"Order #{order.OrderNumber} has been cancelled.";
            return RedirectToAction(nameof(Details), new { id = orderId });
        }

        // POST: /Order/ConfirmPayment (R.7.3 & R.7.4 - Payment confirmation)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmPayment(int orderId, string paymentStatus = "Paid")
        {
            var order = await _context.Orders.FindAsync(orderId);
            if (order == null) return NotFound();

            var currentUserId = GetCurrentUserId();
            bool isAdmin = User.IsInRole("Admin");
            bool isFarmer = (order.FarmerId == currentUserId);
            bool isBuyer = (order.BuyerId == currentUserId);

            if (!isAdmin && !isFarmer && !isBuyer)
            {
                return Forbid();
            }

            await _automationService.ProcessPaymentStatusAsync(orderId, paymentStatus);

            TempData["Success"] = $"Payment for order #{order.OrderNumber} has been recorded as {paymentStatus}.";
            return RedirectToAction(nameof(Details), new { id = orderId });
        }

        private int GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier);
            return claim != null ? int.Parse(claim.Value) : 0;
        }
    }
}
