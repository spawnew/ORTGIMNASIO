using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using GymMembershipApp.Data;
using GymMembershipApp.Models;

namespace GymMembershipApp.Controllers
{
    [Authorize]
    public class AttendanceController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AttendanceController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Attendance
        public async Task<IActionResult> Index(DateTime? startDate, DateTime? endDate, int? memberId)
        {
            ViewData["StartDate"] = startDate?.ToString("yyyy-MM-dd");
            ViewData["EndDate"] = endDate?.ToString("yyyy-MM-dd");
            ViewData["MemberId"] = memberId;

            var attendances = _context.Attendances
                .Include(a => a.Member)
                .AsQueryable();

            // Filter by date range
            if (startDate.HasValue)
            {
                attendances = attendances.Where(a => a.CheckInTime.Date >= startDate.Value.Date);
            }
            if (endDate.HasValue)
            {
                attendances = attendances.Where(a => a.CheckInTime.Date <= endDate.Value.Date);
            }

            // Filter by member
            if (memberId.HasValue)
            {
                attendances = attendances.Where(a => a.MemberId == memberId.Value);
            }

            // Get active members for dropdown
            ViewData["Members"] = new SelectList(
                await _context.Members
                    .Where(m => m.IsActive)
                    .OrderBy(m => m.LastName)
                    .ToListAsync(),
                "Id",
                "FullName",
                memberId);

            return View(await attendances.OrderByDescending(a => a.CheckInTime).ToListAsync());
        }

        // GET: Attendance/CheckIn
        public async Task<IActionResult> CheckIn()
        {
            var activeMembers = await _context.Members
                .Where(m => m.IsActive && m.MembershipEndDate >= DateTime.Today)
                .OrderBy(m => m.LastName)
                .ToListAsync();

            ViewData["MemberId"] = new SelectList(activeMembers, "Id", "FullName");
            return View();
        }

        // POST: Attendance/CheckIn
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckIn([Bind("MemberId,Notes")] Attendance attendance)
        {
            if (ModelState.IsValid)
            {
                // Check if member has active membership
                var member = await _context.Members.FindAsync(attendance.MemberId);
                if (member == null || !member.IsActive || member.MembershipEndDate < DateTime.Today)
                {
                    ModelState.AddModelError("", "Member does not have an active membership.");
                    ViewData["MemberId"] = new SelectList(_context.Members.Where(m => m.IsActive), "Id", "FullName", attendance.MemberId);
                    return View(attendance);
                }

                // Check if member is already checked in
                var existingCheckIn = await _context.Attendances
                    .FirstOrDefaultAsync(a => a.MemberId == attendance.MemberId && a.CheckOutTime == null);

                if (existingCheckIn != null)
                {
                    ModelState.AddModelError("", "Member is already checked in.");
                    ViewData["MemberId"] = new SelectList(_context.Members.Where(m => m.IsActive), "Id", "FullName", attendance.MemberId);
                    return View(attendance);
                }

                attendance.CheckInTime = DateTime.Now;
                _context.Add(attendance);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"{member.FullName} checked in successfully at {attendance.CheckInTime:HH:mm}";
                TempData["HighlightMemberId"] = member.Id;
                return RedirectToAction(nameof(Today));
            }

            ViewData["MemberId"] = new SelectList(_context.Members.Where(m => m.IsActive), "Id", "FullName", attendance.MemberId);
            return View(attendance);
        }

        // GET: Attendance/QuickCheckIn
        public IActionResult QuickCheckIn()
        {
            return View();
        }

        // POST: Attendance/SearchMember
        [HttpPost]
        public async Task<IActionResult> SearchMember([FromBody] SearchRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.SearchTerm))
            {
                return Json(new { success = false, message = "Please enter a search term." });
            }

            var searchTerm = request.SearchTerm.Trim();

            var members = await _context.Members
                .Where(m => m.IsActive &&
                    (m.FirstName.Contains(searchTerm) ||
                     m.LastName.Contains(searchTerm) ||
                     m.Email.Contains(searchTerm) ||
                     m.PhoneNumber.Contains(searchTerm)))
                .Select(m => new
                {
                    id = m.Id,
                    fullName = m.FullName,
                    email = m.Email,
                    membershipEndDate = m.MembershipEndDate,
                    hasActiveMembership = m.MembershipEndDate >= DateTime.Today
                })
                .Take(10)
                .ToListAsync();

            return Json(new { success = true, members });
        }

        public class SearchRequest
        {
            public string SearchTerm { get; set; } = string.Empty;
        }

        // POST: Attendance/ProcessQuickCheckIn
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessQuickCheckIn([FromBody] QuickCheckInRequest request)
        {
            var member = await _context.Members.FindAsync(request.MemberId);
            if (member == null || !member.IsActive)
            {
                return Json(new { success = false, message = "Member not found or inactive." });
            }

            if (member.MembershipEndDate < DateTime.Today)
            {
                return Json(new { success = false, message = "Membership has expired." });
            }

            // Check if already checked in
            var existingCheckIn = await _context.Attendances
                .FirstOrDefaultAsync(a => a.MemberId == request.MemberId && a.CheckOutTime == null);

            if (existingCheckIn != null)
            {
                return Json(new { success = false, message = "Member is already checked in." });
            }

            var attendance = new Attendance
            {
                MemberId = request.MemberId,
                CheckInTime = DateTime.Now
            };

            _context.Add(attendance);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"{member.FullName} checked in successfully at {attendance.CheckInTime:HH:mm}";
            TempData["HighlightMemberId"] = member.Id;

            return Json(new
            {
                success = true,
                message = $"{member.FullName} checked in successfully!",
                checkInTime = attendance.CheckInTime.ToString("HH:mm"),
                redirectUrl = Url.Action(nameof(Today))
            });
        }

        public class QuickCheckInRequest
        {
            public int MemberId { get; set; }
        }

        // POST: Attendance/CheckOut/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckOut(int id)
        {
            var attendance = await _context.Attendances.FindAsync(id);
            if (attendance == null)
            {
                return NotFound();
            }

            if (attendance.CheckOutTime.HasValue)
            {
                return BadRequest("Already checked out.");
            }

            attendance.CheckOutTime = DateTime.Now;
            _context.Update(attendance);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // GET: Attendance/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var attendance = await _context.Attendances
                .Include(a => a.Member)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (attendance == null)
            {
                return NotFound();
            }

            return View(attendance);
        }

        // GET: Attendance/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var attendance = await _context.Attendances
                .Include(a => a.Member)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (attendance == null)
            {
                return NotFound();
            }

            return View(attendance);
        }

        // POST: Attendance/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var attendance = await _context.Attendances.FindAsync(id);
            if (attendance != null)
            {
                _context.Attendances.Remove(attendance);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        // GET: Attendance/Today
        public async Task<IActionResult> Today()
        {
            var today = DateTime.Today;
            var attendances = await _context.Attendances
                .Include(a => a.Member)
                .Where(a => a.CheckInTime.Date == today)
                .OrderByDescending(a => a.CheckInTime)
                .ToListAsync();

            return View(attendances);
        }
    }
}