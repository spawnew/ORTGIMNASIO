using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using GymMembershipApp.Data;
using GymMembershipApp.Models;

namespace GymMembershipApp.Controllers
{
    [Authorize]
    public class PaymentsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<PaymentsController> _logger;

        public PaymentsController(ApplicationDbContext context, ILogger<PaymentsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: Payments
        public async Task<IActionResult> Index(DateTime? startDate, DateTime? endDate, int? memberId, PaymentStatus? status)
        {
            var query = _context.Payments
                .Include(p => p.Member)
                .Include(p => p.MembershipPlan)
                .AsQueryable();

            if (startDate.HasValue)
                query = query.Where(p => p.PaymentDate.Date >= startDate.Value.Date);
            
            if (endDate.HasValue)
                query = query.Where(p => p.PaymentDate.Date <= endDate.Value.Date);
            
            if (memberId.HasValue)
                query = query.Where(p => p.MemberId == memberId.Value);
            
            if (status.HasValue)
                query = query.Where(p => p.Status == status.Value);

            ViewData["Members"] = new SelectList(await _context.Members.OrderBy(m => m.LastName).ToListAsync(), "Id", "FullName");
            ViewData["StartDate"] = startDate?.ToString("yyyy-MM-dd");
            ViewData["EndDate"] = endDate?.ToString("yyyy-MM-dd");
            ViewData["MemberId"] = memberId;
            ViewData["Status"] = status;

            return View(await query.OrderByDescending(p => p.PaymentDate).ToListAsync());
        }

        // GET: Payments/Create
        public IActionResult Create(int? memberId)
        {
            var payment = new Payment();
            if (memberId.HasValue)
            {
                payment.MemberId = memberId.Value;
            }

            ViewData["MemberId"] = new SelectList(_context.Members.OrderBy(m => m.LastName), "Id", "FullName");
            ViewData["MembershipPlanId"] = new SelectList(_context.MembershipPlans.Where(mp => mp.IsActive), "Id", "Name");
            
            return View(payment);
        }

        // POST: Payments/Create
        [HttpPost]
        [ValidateAntiForgeryToken] 
        public async Task<IActionResult> Create([FromForm] Payment payment)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    ViewData["MemberId"] = new SelectList(_context.Members.OrderBy(m => m.LastName), "Id", "FullName");
                    ViewData["MembershipPlanId"] = new SelectList(_context.MembershipPlans.Where(mp => mp.IsActive), "Id", "Name");
                    return View(payment);
                }

                payment.PaymentDate = DateTime.Now;
                _context.Add(payment);
                await _context.SaveChangesAsync();

                if (payment.Status == PaymentStatus.Completed && payment.MembershipPlanId.HasValue)
                {
                    var member = await _context.Members.FindAsync(payment.MemberId);
                    var plan = await _context.MembershipPlans.FindAsync(payment.MembershipPlanId.Value);

                    if (member != null && plan != null)
                    {
                        var startDate = member.MembershipEndDate.HasValue && member.MembershipEndDate > DateTime.Today
                            ? member.MembershipEndDate.Value
                            : DateTime.Today;

                        member.MembershipPlanId = plan.Id;
                        member.MembershipStartDate = startDate;
                        member.MembershipEndDate = startDate.AddDays(plan.DurationInDays);
                        member.IsActive = true;

                        _context.Update(member);
                        await _context.SaveChangesAsync();
                    }
                }

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating payment");
                ModelState.AddModelError("", "Error creating payment: " + ex.Message);
                ViewData["MemberId"] = new SelectList(_context.Members.OrderBy(m => m.LastName), "Id", "FullName");
                ViewData["MembershipPlanId"] = new SelectList(_context.MembershipPlans.Where(mp => mp.IsActive), "Id", "Name");
                return View(payment);
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetPlanPrice(int id)
        {
            var plan = await _context.MembershipPlans.FindAsync(id);
            return Json(plan?.Price ?? 0);
        }

        // GET: Payments/DueMembers
        public async Task<IActionResult> DueMembers()
        {
            var today = DateTime.Today;
            var dueDate = today.AddDays(7);

            var dueMembers = await _context.Members
                .Include(m => m.MembershipPlan)
                .Where(m => m.IsActive &&
                            m.MembershipEndDate.HasValue &&
                            m.MembershipEndDate >= today &&
                            m.MembershipEndDate <= dueDate)
                .OrderBy(m => m.MembershipEndDate)
                .ToListAsync();

            return View(dueMembers);
        }

        // GET: Payments/Pending
        public async Task<IActionResult> Pending()
        {
            var pendingPayments = await _context.Payments
                .Include(p => p.Member)
                .Include(p => p.MembershipPlan)
                .Where(p => p.Status == PaymentStatus.Pending)
                .OrderByDescending(p => p.PaymentDate)
                .ToListAsync();

            return View(pendingPayments);
        }

        // POST: Payments/UpdateStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int id, PaymentStatus status)
        {
            var payment = await _context.Payments
                .Include(p => p.Member)
                .Include(p => p.MembershipPlan)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (payment == null)
            {
                return NotFound();
            }

            payment.Status = status;
            _context.Update(payment);
            await _context.SaveChangesAsync();

            // If payment is completed and has a membership plan, update member's membership
            if (status == PaymentStatus.Completed && payment.MembershipPlanId.HasValue)
            {
                var member = payment.Member;
                var plan = payment.MembershipPlan;

                if (member != null && plan != null)
                {
                    var startDate = member.MembershipEndDate.HasValue && member.MembershipEndDate > DateTime.Today
                        ? member.MembershipEndDate.Value
                        : DateTime.Today;

                    member.MembershipPlanId = plan.Id;
                    member.MembershipStartDate = startDate;
                    member.MembershipEndDate = startDate.AddDays(plan.DurationInDays);
                    member.IsActive = true;

                    _context.Update(member);
                    await _context.SaveChangesAsync();
                }
            }

            TempData["SuccessMessage"] = $"Payment status updated to {status}";
            return RedirectToAction(nameof(Pending));
        }
    }
}
