using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using DeTaiCuoiKy_Nhom6.Data;
using DeTaiCuoiKy_Nhom6.Models;

namespace DeTaiCuoiKy_Nhom6.Controllers
{
    /// <summary>
    /// Controller quản lý toàn bộ nghiệp vụ liên quan đến Công việc (Todo Tasks)
    /// Hỗ trợ phân quyền chặt chẽ giữa Admin và User, tích hợp AI Chatbot và hệ thống cảnh báo Real-time.
    /// </summary>
    [Authorize]
    public class CongViecController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<CongViecController> _logger;

        /// <summary>
        /// Khởi tạo Controller với kỹ thuật Dependency Injection quản lý Context, User và Logger
        /// </summary>
        public CongViecController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            ILogger<CongViecController> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        /// <summary>
        /// Hàm helper lấy thông tin User hiện tại đăng nhập một cách an toàn
        /// </summary>
        private Task<ApplicationUser> GetCurrentUserAsync() => _userManager.GetUserAsync(HttpContext.User);

        /// <summary>
        /// GET: CongViec
        /// Hiển thị danh sách công việc có tích hợp Thống kê, Bộ lọc đa điều kiện, Sắp xếp nâng cao và Phân trang.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index(
            string? searchString,
            string? statusFilter,
            string? typeFilter,
            string? timeFilter,
            string? sortBy,
            int pageNumber = 1,
            int pageSize = 10)
        {
            _logger.LogInformation("Xử lý yêu cầu Index danh sách công việc bởi User: {User}", User.Identity?.Name);
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                _logger.LogWarning("Không tìm thấy thông tin phiên đăng nhập hợp lệ. Đẩy về trang Challenge.");
                return Challenge();
            }

            var roles = await _userManager.GetRolesAsync(user);
            var isAdmin = roles.Contains("Admin");

            // PHÂN QUYỀN TRUY VẤN: Admin thấy toàn bộ hệ thống, User chỉ thấy việc của mình
            IQueryable<CongViec> query = isAdmin
                ? _context.CongViecs
                : _context.CongViecs.Where(c => c.UserId == user.Id);

            // --- TÍNH TOÁN SỐ LIỆU THỐNG KÊ (Trước khi áp dụng bộ lọc hiển thị nhưng dựa trên quyền xem) ---
            var todayDate = DateTime.Today;
            ViewData["StatTotal"] = await query.CountAsync();
            ViewData["StatCompleted"] = await query.CountAsync(c => c.DaHoanThanh);
            ViewData["StatOverdue"] = await query.CountAsync(c => c.NgayHetHan.Date < todayDate && !c.DaHoanThanh);

            // Giữ lại trạng thái bộ lọc trên giao diện UI thông qua ViewData
            ViewData["CurrentSearch"] = searchString;
            ViewData["CurrentStatus"] = statusFilter;
            ViewData["CurrentType"] = typeFilter;
            ViewData["CurrentTime"] = timeFilter;
            ViewData["CurrentSort"] = sortBy;

            // 1. Bộ lọc tìm kiếm theo từ khóa văn bản
            if (!string.IsNullOrEmpty(searchString))
            {
                searchString = searchString.Trim();
                query = query.Where(s => s.TenCongViec.Contains(searchString) || (s.MoTa != null && s.MoTa.Contains(searchString)));
            }

            // 2. Bộ lọc trạng thái xử lý
            if (!string.IsNullOrEmpty(statusFilter))
            {
                bool isCompleted = statusFilter == "completed";
                query = query.Where(s => s.DaHoanThanh == isCompleted);
            }

            // 3. Bộ lọc theo danh mục / Phân loại lĩnh vực
            if (!string.IsNullOrEmpty(typeFilter))
            {
                query = query.Where(s => s.PhanLoai == typeFilter);
            }

            // 4. Bộ lọc mốc thời gian Deadline
            if (!string.IsNullOrEmpty(timeFilter))
            {
                if (timeFilter == "today")
                    query = query.Where(s => s.NgayHetHan.Date == todayDate);
                else if (timeFilter == "overdue")
                    query = query.Where(s => s.NgayHetHan.Date < todayDate && !s.DaHoanThanh);
                else if (timeFilter == "upcoming")
                    query = query.Where(s => s.NgayHetHan.Date > todayDate);
            }

            // 5. Hệ thống sắp xếp linh hoạt (Sorting)
            query = sortBy switch
            {
                "date_desc" => query.OrderByDescending(c => c.NgayHetHan),
                "title_asc" => query.OrderBy(c => c.TenCongViec),
                "title_desc" => query.OrderByDescending(c => c.TenCongViec),
                _ => query.OrderBy(c => c.NgayHetHan) // Mặc định: Hạn chót gần nhất lên trước
            };

            // 6. Cơ chế phân trang dữ liệu an toàn chặn lỗi số âm
            if (pageNumber < 1) pageNumber = 1;
            int totalItems = await query.CountAsync();
            int totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
            if (totalPages == 0) totalPages = 1;

            ViewData["PageNumber"] = pageNumber;
            ViewData["TotalPages"] = totalPages;
            ViewData["HasPreviousPage"] = pageNumber > 1;
            ViewData["HasNextPage"] = pageNumber < totalPages;

            var danhSachPhanTrang = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return View(danhSachPhanTrang);
        }

        /// <summary>
        /// POST: CongViec/TaoNgauNhien
        /// API nghiệp vụ nâng cao: Tự động phân tích và sinh ngẫu nhiên công việc thực tế theo nhóm ngành.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> TaoNgauNhien()
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                _logger.LogWarning("Yêu cầu tạo việc ngẫu nhiên bị từ chối do hết phiên làm việc.");
                return Json(new { success = false, message = "Hết phiên đăng nhập. Vui lòng đăng nhập lại!" });
            }

            // Kho dữ liệu mẫu quy chuẩn phục vụ đồ án đa lĩnh vực
            var khoCongViec = new Dictionary<string, List<(string Ten, string MoTa)>>()
            {
                { "Văn phòng", new List<(string, string)> {
                    ("Soạn thảo biên bản cuộc họp", "Tổng hợp lại toàn bộ nội dung cuộc họp tuần này và gửi cho sếp."),
                    ("Sắp xếp lại tủ hồ sơ dự án", "Phân loại tài liệu cũ và tiến hành số hóa đưa lên hệ thống lưu trữ cloud."),
                    ("Kiểm kê văn phòng phẩm", "Lập danh sách bút, giấy, mực in cần mua bổ sung cho tháng tới.")
                }},
                { "Công sở", new List<(string, string)> {
                    ("Chuẩn bị tài liệu tiếp đoàn", "Sắp xếp hồ sơ pháp lý kiểm tra cơ quan, chuẩn bị phòng họp số 2."),
                    ("Tham gia tập huấn PCCC", "Tham gia diễn tập phòng cháy chữa cháy định kỳ tại tòa nhà cơ quan."),
                    ("Lập báo cáo tiến độ quý", "Thu thập số liệu từ các phòng ban để hoàn thiện báo cáo tổng hợp.")
                }},
                { "Giáo dục", new List<(string, string)> {
                    ("Soạn giáo án bài giảng mới", "Chuẩn bị bài giảng slide PowerPoint và tài liệu phát tay cho học viên."),
                    ("Chấm điểm bài thi giữa kỳ", "Chấm điểm và nhập kết quả lên hệ thống quản lý học tập điện tử."),
                    ("Nội dung họp phụ huynh", "Lập danh sách học sinh cần tuyên dương và lưu ý đặc biệt gửi phụ huynh.")
                }},
                { "Y tế", new List<(string, string)> {
                    ("Kiểm tra hạn dùng tủ thuốc", "Rà soát lại toàn bộ thuốc cấp cứu, loại bỏ thuốc cận date."),
                    ("Cập nhật hồ sơ bệnh án", "Số hóa thông tin lịch sử khám chữa bệnh của các bệnh nhân trong tuần."),
                    ("Lên lịch trực ca tuần sau", "Phân chia ca trực ngày/đêm cho các y bác sĩ trực thuộc khoa.")
                }},
                { "Marketing", new List<(string, string)> {
                    ("Ý tưởng chiến dịch mới", "Draft outline kế hoạch truyền thông tích hợp cho sản phẩm sắp ra mắt."),
                    ("Viết content bài đăng Fanpage", "Viết chuỗi bài đăng kèm hashtag và định hướng hình ảnh truyền thông."),
                    ("Phân tích Google Analytics", "Đánh giá lượng truy cập tuần qua và tối ưu hóa từ khóa SEO.")
                }}
            };

            var random = new Random();
            var danhSachLinhVuc = khoCongViec.Keys.ToList();
            string linhVucNgauNhien = danhSachLinhVuc[random.Next(danhSachLinhVuc.Count)];
            var danhSachTask = khoCongViec[linhVucNgauNhien];
            var taskNgauNhien = danhSachTask[random.Next(danhSachTask.Count)];

            // NÂNG CẤP: Tính toán xác suất xuất hiện hạn chót dưới 5 phút (Xác suất thấp: 10%)
            DateTime hanChotNgauNhien;
            if (random.Next(1, 101) <= 10) // 10% cơ hội rơi vào tình huống khẩn cấp reo chuông
            {
                hanChotNgauNhien = DateTime.Now.AddMinutes(random.Next(2, 5));
            }
            else
            {
                hanChotNgauNhien = DateTime.Now.AddDays(random.Next(1, 8)).AddHours(random.Next(1, 12));
            }

            var cvMoi = new CongViec
            {
                TenCongViec = taskNgauNhien.Ten,
                MoTa = taskNgauNhien.MoTa,
                NgayHetHan = hanChotNgauNhien,
                DaHoanThanh = false,
                PhanLoai = linhVucNgauNhien,
                UserId = user.Id,
                CreatedByUserId = user.Id
            };

            try
            {
                _context.CongViecs.Add(cvMoi);
                await _context.SaveChangesAsync();
                _logger.LogInformation("User {UserId} đã sinh ngẫu nhiên thành công công việc ID {TaskId}", user.Id, cvMoi.Id);
                return Json(new { success = true, message = $"Hệ thống vừa phân cho bạn một việc ngẫu nhiên thuộc lĩnh vực [{linhVucNgauNhien}]!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi nghiêm trọng khi thêm công việc ngẫu nhiên vào database.");
                return Json(new { success = false, message = "Lỗi hệ thống khi lưu dữ liệu: " + ex.Message });
            }
        }

        /// <summary>
        /// POST: CongViec/ToggleStatus/5
        /// Thay đổi nhanh trạng thái hoàn thành của công việc + Tích hợp Gamification tính điểm XP.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Challenge();

            var congViec = await _context.CongViecs.FindAsync(id);
            if (congViec == null) return NotFound();

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

            if (congViec.UserId == user.Id || isAdmin)
            {
                bool trangThaiMoi = !congViec.DaHoanThanh;
                congViec.DaHoanThanh = trangThaiMoi;

                // LOGIC GAMIFICATION: Tính toán điểm số dựa vào mốc thời gian hoàn thành
                var chuSoHuu = await _userManager.FindByIdAsync(congViec.UserId);
                if (chuSoHuu != null)
                {
                    if (trangThaiMoi) // Nếu đánh dấu HOÀN THÀNH
                    {
                        var thoiGianConLai = congViec.NgayHetHan - DateTime.Now;

                        if (thoiGianConLai.TotalMinutes > 0)
                        {
                            if (thoiGianConLai.TotalMinutes <= 5)
                            {
                                chuSoHuu.DiemXP += 10; // Hạn còn dưới 5 phút: cộng ít điểm
                                TempData["ToastMessage"] = "Hoàn thành cận hạn (< 5 phút)! Được cộng +10 XP. 🕒";
                            }
                            else
                            {
                                chuSoHuu.DiemXP += 30; // Hoàn thành sớm hạn: cộng nhiều điểm
                                TempData["ToastMessage"] = "Tuyệt vời! Hoàn thành sớm hạn được cộng +30 XP! 🚀";
                            }
                        }
                        else
                        {
                            chuSoHuu.DiemXP = Math.Max(0, chuSoHuu.DiemXP - 15); // Đã hết hạn mà chưa làm: Bị trừ điểm
                            TempData["ToastMessage"] = "Công việc đã hết hạn từ trước! Bị trừ -15 XP. ⚠️";
                        }

                        TempData["ShowFireworks"] = "true"; // Bật cờ kích hoạt hiệu ứng bắn pháo hoa ở View
                    }
                    else // Nếu hủy đánh dấu hoàn thành (chuyển ngược lại chưa xong)
                    {
                        // Hoàn trả điểm hoặc trừ phạt tùy ý, ở đây giữ nguyên hoặc trừ nhẹ tránh lỗi lạm dụng bug điểm
                        chuSoHuu.DiemXP = Math.Max(0, chuSoHuu.DiemXP - 10);
                        TempData["ToastMessage"] = "Đã chuyển việc về trạng thái CHƯA XONG! Trừ bớt 10 XP. 🕒";
                    }

                    await _userManager.UpdateAsync(chuSoHuu);
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("Công việc {TaskId} đã được đổi trạng thái thành {Status}", id, congViec.DaHoanThanh);
            }
            else
            {
                _logger.LogWarning("User {UserId} cố gắng thay đổi trạng thái Task trái phép của {OwnerId}", user.Id, congViec.UserId);
                return RedirectToAction("AccessDenied", "Account");
            }
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// GET: CongViec/GetAdminUserStats
        /// API bảo mật cung cấp chuỗi số liệu cấu trúc JSON cho đồ thị thống kê Admin Settings.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAdminUserStats()
        {
            var user = await GetCurrentUserAsync();
            if (user == null || !(await _userManager.IsInRoleAsync(user, "Admin")))
            {
                return Forbid();
            }

            var now = DateTime.Now;
            var danhSachUser = await _userManager.Users.ToListAsync();
            var duLieuBieuDo = new List<object>();

            foreach (var u in danhSachUser)
            {
                var danhSachViec = await _context.CongViecs.Where(c => c.UserId == u.Id).ToListAsync();

                int hoanThanhSom = danhSachViec.Count(c => c.DaHoanThanh && c.NgayHetHan > now.AddMinutes(5));
                int hoanThanhMuon = danhSachViec.Count(c => c.DaHoanThanh && c.NgayHetHan <= now.AddMinutes(5));
                int chuaLam = danhSachViec.Count(c => !c.DaHoanThanh);

                duLieuBieuDo.Add(new
                {
                    email = u.Email,
                    hoanThanhSom,
                    hoanThanhMuon,
                    chuaLam
                });
            }

            return Json(duLieuBieuDo);
        }

        /// <summary>
        /// GET: CongViec/Create
        /// Điều hướng hiển thị giao diện tạo mới công việc thủ công. Có nạp danh sách tài khoản hệ thống để giao việc.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var danhSachThanhVien = await _userManager.Users.ToListAsync();
            ViewBag.UserList = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(danhSachThanhVien, "Id", "Email");
            return View();
        }

        /// <summary>
        /// POST: CongViec/Create
        /// Xử lý dữ liệu Form nhận về để khởi tạo một bản ghi công việc mới.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,TenCongViec,MoTa,NgayHetHan,DaHoanThanh,PhanLoai,UserId,MaTranEisenhower")] CongViec congViec)
        {
            if (ModelState.IsValid)
            {
                var user = await GetCurrentUserAsync();
                if (user == null) return Challenge();
                if (string.IsNullOrEmpty(congViec.UserId))
                {
                    congViec.UserId = user.Id;
                }
                congViec.CreatedByUserId = user.Id;
                _context.Add(congViec);
                await _context.SaveChangesAsync();
                TempData["ToastMessage"] = "Thêm mới và giao công việc thành công rực rỡ! 🚀";
                return RedirectToAction(nameof(Index));
            }
            var users = await _userManager.Users.ToListAsync();
            ViewBag.UserList = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(users, "Id", "Email", congViec.UserId);
            return View(congViec);
        }

        /// <summary>
        /// GET: CongViec/Edit/5
        /// Kiểm tra điều kiện bảo mật và lấy thông tin đưa lên form chỉnh sửa.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var user = await GetCurrentUserAsync();
            if (user == null) return Challenge();
            var congViec = await _context.CongViecs.FindAsync(id);
            if (congViec == null) return NotFound();
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            if (!isAdmin && (congViec.UserId != user.Id || congViec.CreatedByUserId != user.Id))
            {
                return RedirectToAction("AccessDenied", "Account");
            }
            return View(congViec);
        }

        /// <summary>
        /// POST: CongViec/Edit/5
        /// Cập nhật thông tin chi tiết công việc.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,TenCongViec,MoTa,NgayHetHan,DaHoanThanh,PhanLoai")] CongViec congViec)
        {
            if (id != congViec.Id) return NotFound();
            var user = await GetCurrentUserAsync();
            if (user == null) return Challenge();
            var bieuMauGoc = await _context.CongViecs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (bieuMauGoc == null) return NotFound();
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            if (!isAdmin && (bieuMauGoc.UserId != user.Id || bieuMauGoc.CreatedByUserId != user.Id))
            {
                return RedirectToAction("AccessDenied", "Account");
            }
            if (ModelState.IsValid)
            {
                try
                {
                    congViec.UserId = bieuMauGoc.UserId;
                    congViec.CreatedByUserId = bieuMauGoc.CreatedByUserId;
                    _context.Update(congViec);
                    await _context.SaveChangesAsync();
                    TempData["ToastMessage"] = "Cập nhật dữ liệu công việc thành công! 💾";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!CongViecExists(congViec.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(congViec);
        }

        /// <summary>
        /// GET: CongViec/Delete/5
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            var user = await GetCurrentUserAsync();
            if (user == null) return Challenge();
            var congViec = await _context.CongViecs.FirstOrDefaultAsync(m => m.Id == id);
            if (congViec == null) return NotFound();
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            if (!isAdmin && (congViec.UserId != user.Id || congViec.CreatedByUserId != user.Id))
            {
                return RedirectToAction("AccessDenied", "Account");
            }
            return View(congViec);
        }

        /// <summary>
        /// POST: CongViec/Delete/5
        /// </summary>
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Challenge();
            var congViec = await _context.CongViecs.FindAsync(id);
            if (congViec == null) return NotFound();
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            if (!isAdmin && (congViec.UserId != user.Id || congViec.CreatedByUserId != user.Id))
            {
                return RedirectToAction("AccessDenied", "Account");
            }
            _context.CongViecs.Remove(congViec);
            await _context.SaveChangesAsync();
            TempData["ToastMessage"] = "Đã xóa công việc khỏi hệ thống! 🗑️";
            return RedirectToAction(nameof(Index));
        }

        private bool CongViecExists(int id)
        {
            return _context.CongViecs.Any(e => e.Id == id);
        }

        [HttpGet]
        public async Task<IActionResult> GetUpcomingDeadlines()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Json(new List<object>());
            var now = DateTime.Now;
            var limitTime = now.AddMinutes(5);
            var upcomingTasks = await _context.CongViecs
                .Where(c => c.UserId == user.Id && !c.DaHoanThanh && c.NgayHetHan > now && c.NgayHetHan <= limitTime)
                .Select(c => new { id = c.Id, ten = c.TenCongViec, han = c.NgayHetHan.ToString("HH:mm") })
                .ToListAsync();
            return Json(upcomingTasks);
        }

        [HttpPost]
        public async Task<IActionResult> AskChatbot(string message)
        {
            if (string.IsNullOrEmpty(message))
                return Json(new { reply = "Chào bạn! Bạn có câu hỏi nào cần Trợ lý ToDoTech giải đáp không ạ? 🤖" });
            var user = await GetCurrentUserAsync();
            string userId = user?.Id ?? "";
            int tongViec = 0, daXong = 0, treHan = 0;
            if (!string.IsNullOrEmpty(userId))
            {
                var baseQuery = _context.CongViecs.Where(c => c.UserId == userId);
                tongViec = await baseQuery.CountAsync();
                daXong = await baseQuery.CountAsync(c => c.DaHoanThanh);
                treHan = await baseQuery.CountAsync(c => c.NgayHetHan.Date < DateTime.Today && !c.DaHoanThanh);
            }
            string input = message.ToLower().Trim();
            string reply = "";
            if (input.Contains("thống kê") || input.Contains("bao nhiêu việc") || input.Contains("báo cáo") || input.Contains("tình hình"))
            {
                reply = $"📊 **Thống kê công việc hiện tại của bạn:**\n\n" +
                        $"- Tổng số công việc: **{tongViec}** nhiệm vụ.\n" +
                        $"- Đã hoàn thành: **{daXong}** việc. 🎉\n" +
                        $"- Đang trễ hạn chót: <span class='text-danger fw-bold'>{treHan}</span> việc. 🕒\n\n" +
                        $"Hãy sắp xếp thời gian hợp lý để hoàn thành các công việc trễ hạn bạn nhé!";
            }
            else if (input.Contains("tính năng") || input.Contains("chức năng") || input.Contains("làm được gì") || input.Contains("hệ thống"))
            {
                reply = $"🚀 **Hệ thống ToDoTech sở hữu các tính năng cao cấp sau:**\n\n" +
                        $"1. **Quản lý công việc:** Thao tác CRUD cốt lõi, tích hợp bộ lọc thông minh đa chế độ và thuật toán phân trang.\n" +
                        $"2. **Sinh việc tự động:** Cơ chế bốc ngẫu nhiên việc đa ngành thực tế.\n" +
                        $"3. **Cảnh báo chuông reo độc quyền:** Kỹ thuật Polling ngầm phối hợp **Web Audio API** reo chuông báo thức.\n" +
                        $"4. **Hệ thống Gamification:** Tích hợp tính điểm XP, nâng cấp level, phân loại danh hiệu danh tiếng thành viên.\n" +
                        $"5. **Admin Dashboard:** Biểu đồ Chart.js phân tích đa chiều hiệu suất hoàn thành của từng cá nhân.";
            }
            else
            {
                reply = $"🤖 **Trợ lý ảo ghi nhận câu hỏi của bạn:** *\"{message}\"*\n\nNếu cần trợ giúp sâu về thuật toán, bạn hãy liên hệ trực tiếp các thành viên Nhóm 6 nhé!";
            }
            return Json(new { reply });
        }
    }
}