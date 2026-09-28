using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AgriDirect.Data;
using AgriDirect.Models;
using AgriDirect.Models.ViewModels;
using AgriDirect.Services;

namespace AgriDirect.Controllers
{
    public class AccountController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IWebHostEnvironment _environment;
        private readonly ISystemAutomationService _automationService;

        public AccountController(
            AppDbContext context,
            IPasswordHasher passwordHasher,
            IWebHostEnvironment environment,
            ISystemAutomationService automationService)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _environment = environment;
            _automationService = automationService;
        }

        // GET: /Account/Register
        [HttpGet]
        public IActionResult Register()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Marketplace");
            }
            return View(new RegisterViewModel());
        }

        // POST: /Account/Register
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var emailExists = await _context.Users.AnyAsync(u => u.Email.ToLower() == model.Email.ToLower());
            if (emailExists)
            {
                ModelState.AddModelError("Email", "An account with this email address already exists.");
                return View(model);
            }

            var defaultAvatar = model.Role == "Farmer" ? "/images/farmer-rajesh.svg" : "/images/buyer-rahul.svg";

            var user = new User
            {
                FullName = model.FullName.Trim(),
                Email = model.Email.Trim().ToLower(),
                PasswordHash = _passwordHasher.HashPassword(model.Password),
                PhoneNumber = model.PhoneNumber.Trim(),
                Address = model.Address.Trim(),
                Role = (model.Role == "Farmer") ? "Farmer" : "Consumer",
                FarmName = model.Role == "Farmer" ? model.FarmName?.Trim() : null,
                FarmLocation = model.Role == "Farmer" ? model.FarmLocation?.Trim() : null,
                ProfilePictureUrl = defaultAvatar,
                Status = "Active",
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Automated welcome notification
            await _automationService.SendNotificationAsync(
                user.Id,
                "🌾 Welcome to AgriDirect!",
                $"Hello {user.FullName}, your {user.Role} account has been created successfully. Explore direct farm-to-table trade today!",
                "Account",
                user.Role == "Farmer" ? "/Product/MyProducts" : "/Marketplace"
            );

            // Sign in automatically
            await SignInUserAsync(user, isPersistent: false);

            TempData["Success"] = $"Welcome to AgriDirect, {user.FullName}! Your account has been created.";

            if (user.Role == "Farmer")
            {
                return RedirectToAction("FarmerDashboard", "Account");
            }
            return RedirectToAction("ConsumerDashboard", "Account");
        }

        // GET: /Account/Login
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Marketplace");
            }
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        // POST: /Account/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == model.Email.ToLower());
            if (user == null || !_passwordHasher.VerifyPassword(model.Password, user.PasswordHash))
            {
                ModelState.AddModelError(string.Empty, "Invalid email address or password.");
                return View(model);
            }

            if (user.Status == "Blocked")
            {
                ModelState.AddModelError(string.Empty, "Your account has been suspended by an administrator. Please contact support.");
                return View(model);
            }

            if (user.Status == "Deactivated")
            {
                ModelState.AddModelError(string.Empty, "Your account is currently deactivated. Please contact support to reactivate.");
                return View(model);
            }

            await SignInUserAsync(user, model.RememberMe);

            TempData["Success"] = $"Welcome back, {user.FullName}!";

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }

            if (user.Role == "Admin")
            {
                return RedirectToAction("Dashboard", "Admin");
            }
            else if (user.Role == "Farmer")
            {
                return RedirectToAction("FarmerDashboard", "Account");
            }
            return RedirectToAction("ConsumerDashboard", "Account");
        }

        // GET: /Account/FarmerDashboard
        [Authorize(Roles = "Farmer")]
        [HttpGet]
        public async Task<IActionResult> FarmerDashboard()
        {
            var userId = GetCurrentUserId();
            var farmer = await _context.Users
                .Include(u => u.Products)
                .Include(u => u.FarmerOrders)
                    .ThenInclude(o => o.Product)
                .Include(u => u.FarmerOrders)
                    .ThenInclude(o => o.Buyer)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (farmer == null) return NotFound();
            return View(farmer);
        }

        // GET: /Account/ConsumerDashboard
        [Authorize(Roles = "Consumer")]
        [HttpGet]
        public async Task<IActionResult> ConsumerDashboard()
        {
            var userId = GetCurrentUserId();
            var consumer = await _context.Users
                .Include(u => u.BuyerOrders)
                    .ThenInclude(o => o.Product)
                .Include(u => u.BuyerOrders)
                    .ThenInclude(o => o.Farmer)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (consumer == null) return NotFound();
            return View(consumer);
        }

        // GET: /Account/Profile
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var userId = GetCurrentUserId();
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            var model = new ProfileViewModel
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                Address = user.Address,
                Role = user.Role,
                ProfilePictureUrl = user.ProfilePictureUrl,
                FarmName = user.FarmName,
                FarmLocation = user.FarmLocation,
                AverageRating = user.AverageRating,
                TotalRatingsCount = user.TotalRatingsCount,
                Status = user.Status
            };

            return View(model);
        }

        // POST: /Account/Profile
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(ProfileViewModel model, IFormFile? profilePicture)
        {
            var userId = GetCurrentUserId();
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            if (!ModelState.IsValid)
            {
                model.Email = user.Email;
                model.Role = user.Role;
                model.ProfilePictureUrl = user.ProfilePictureUrl;
                model.AverageRating = user.AverageRating;
                model.TotalRatingsCount = user.TotalRatingsCount;
                return View(model);
            }

            user.FullName = model.FullName.Trim();
            user.PhoneNumber = model.PhoneNumber.Trim();
            user.Address = model.Address.Trim();

            if (user.Role == "Farmer")
            {
                user.FarmName = model.FarmName?.Trim();
                user.FarmLocation = model.FarmLocation?.Trim();
            }

            if (profilePicture != null && profilePicture.Length > 0)
            {
                var uploadsDir = Path.Combine(_environment.WebRootPath, "uploads", "avatars");
                Directory.CreateDirectory(uploadsDir);

                var fileName = $"avatar_{user.Id}_{Guid.NewGuid().ToString()[..8]}{Path.GetExtension(profilePicture.FileName)}";
                var filePath = Path.Combine(uploadsDir, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await profilePicture.CopyToAsync(stream);
                }

                user.ProfilePictureUrl = $"/uploads/avatars/{fileName}";
            }

            await _context.SaveChangesAsync();

            // Refresh cookie claims if name changed
            await SignInUserAsync(user, isPersistent: false);

            TempData["Success"] = "Your profile has been updated successfully!";
            return RedirectToAction(nameof(Profile));
        }

        // POST: /Account/Deactivate
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deactivate()
        {
            var userId = GetCurrentUserId();
            var user = await _context.Users.FindAsync(userId);
            if (user != null)
            {
                user.Status = "Deactivated";
                await _context.SaveChangesAsync();

                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                TempData["Info"] = "Your account has been deactivated. You can contact support anytime to reactivate it.";
            }
            return RedirectToAction("Index", "Marketplace");
        }

        // POST: /Account/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["Info"] = "You have been logged out successfully.";
            return RedirectToAction("Index", "Marketplace");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        private async Task SignInUserAsync(User user, bool isPersistent)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("ProfilePicture", user.ProfilePictureUrl),
                new Claim("FarmName", user.FarmName ?? "")
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = isPersistent,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);
        }

        private int GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier);
            return claim != null ? int.Parse(claim.Value) : 0;
        }
    }
}
