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

            // Thiết lập hạn chót linh động từ 1 đến 7 ngày tới
            DateTime hanChotNgauNhien = DateTime.Now.AddDays(random.Next(1, 8));

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
                return Json(new { success = true, message = $"Hệ thống vừa phân cho bạn một việc thuộc lĩnh vực [{linhVucNgauNhien}]!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi nghiêm trọng khi thêm công việc ngẫu nhiên vào database.");
                return Json(new { success = false, message = "Lỗi hệ thống khi lưu dữ liệu: " + ex.Message });
            }
        }

        /// <summary>
        /// POST: CongViec/ToggleStatus/5
        /// Thay đổi nhanh trạng thái hoàn thành của công việc thông qua cơ chế nhấn Checkbox hoặc Button.
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

            // Kiểm soát an ninh vòng lặp: Chỉ chủ sở hữu hoặc Admin tối cao mới được đổi trạng thái
            if (congViec.UserId == user.Id || isAdmin)
            {
                congViec.DaHoanThanh = !congViec.DaHoanThanh;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Công việc {TaskId} đã được đổi trạng thái thành {Status}", id, congViec.DaHoanThanh);
                TempData["ToastMessage"] = congViec.DaHoanThanh ? "Đã đánh dấu HOÀN THÀNH công việc! 🎉" : "Đã chuyển việc về trạng thái CHƯA XONG! 🕒";
            }
            else
            {
                _logger.LogWarning("User {UserId} cố gắng thay đổi trạng thái Task trái phép của {OwnerId}", user.Id, congViec.UserId);
                return RedirectToAction("AccessDenied", "Account");
            }
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// GET: CongViec/Create
        /// Điều hướng hiển thị giao diện tạo mới công việc thủ công.
        /// </summary>
        [HttpGet]
        public IActionResult Create() => View();

        /// <summary>
        /// POST: CongViec/Create
        /// Xử lý dữ liệu Form nhận về để khởi tạo một bản ghi công việc mới.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,TenCongViec,MoTa,NgayHetHan,DaHoanThanh,PhanLoai,UserId")] CongViec congViec)
        {
            if (ModelState.IsValid)
            {
                var user = await GetCurrentUserAsync();
                if (user == null) return Challenge();

                var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

                // Ngăn chặn User thường giả mạo gán việc cho người khác qua DevTools Inject
                if (string.IsNullOrEmpty(congViec.UserId) || !isAdmin)
                {
                    congViec.UserId = user.Id;
                }

                // Lưu vết người trực tiếp nhấn nút tạo công việc này
                congViec.CreatedByUserId = user.Id;

                _context.Add(congViec);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Thêm mới thành công công việc thủ công: {Title}", congViec.TenCongViec);
                TempData["ToastMessage"] = "Thêm mới công việc thành công rực rỡ! 🚀";
                return RedirectToAction(nameof(Index));
            }
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

            // KIỂM TRA BẢO MẬT ĐƯỜNG DẪN (Anti-Backdoor): Phải là Admin HOẶC chính chủ tạo mới có quyền truy cập Form sửa
            if (!isAdmin && (congViec.UserId != user.Id || congViec.CreatedByUserId != user.Id))
            {
                _logger.LogWarning("Truy cập trái phép Form Edit bởi User {User} tại Task {TaskId}", user.Id, id);
                return RedirectToAction("AccessDenied", "Account");
            }

            return View(congViec);
        }

        /// <summary>
        /// POST: CongViec/Edit/5
        /// Cập nhật thông tin chi tiết công việc, thực thi kỹ thuật chống tấn công ghi đè thuộc tính phân quyền.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,TenCongViec,MoTa,NgayHetHan,DaHoanThanh,PhanLoai")] CongViec congViec)
        {
            if (id != congViec.Id) return NotFound();

            var user = await GetCurrentUserAsync();
            if (user == null) return Challenge();

            // Truy vấn lấy bản ghi không theo vết (AsNoTracking) từ DB để đối chiếu dữ liệu gốc an toàn
            var bieuMauGoc = await _context.CongViecs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (bieuMauGoc == null) return NotFound();

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

            if (!isAdmin && (bieuMauGoc.UserId != user.Id || bieuMauGoc.CreatedByUserId != user.Id))
            {
                _logger.LogWarning("Hành vi chỉnh sửa dữ liệu bất hợp pháp bị chặn tại Task {TaskId}", id);
                return RedirectToAction("AccessDenied", "Account");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // KIÊN QUYẾT giữ nguyên các trường phân quyền gốc, tránh lộ lọt cấu trúc dữ liệu qua Form đè
                    congViec.UserId = bieuMauGoc.UserId;
                    congViec.CreatedByUserId = bieuMauGoc.CreatedByUserId;

                    _context.Update(congViec);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation("Cập nhật thành công công việc ID {TaskId}", congViec.Id);
                    TempData["ToastMessage"] = "Cập nhật dữ liệu công việc thành công! 💾";
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    _logger.LogError(ex, "Lỗi xung đột đồng thời khi cập nhật Task {TaskId}", congViec.Id);
                    if (!CongViecExists(congViec.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(congViec);
        }

        /// <summary>
        /// GET: CongViec/Delete/5
        /// Hiển thị màn hình xác nhận xóa công việc nếu đủ thẩm quyền bảo mật.
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
        /// Xác nhận hành động hủy và xóa hoàn toàn bản ghi công việc khỏi cơ sở dữ liệu.
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

            // Chốt chặn an ninh tối cao tầng dữ liệu trước lệnh Remove
            if (!isAdmin && (congViec.UserId != user.Id || congViec.CreatedByUserId != user.Id))
            {
                _logger.LogCritical("CẢNH BÁO: Tấn công giả mạo yêu cầu xóa Task {TaskId} bởi User {UserId}", id, user.Id);
                return RedirectToAction("AccessDenied", "Account");
            }

            _context.CongViecs.Remove(congViec);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Xóa thành công công việc ID {TaskId} khỏi hệ thống", id);
            TempData["ToastMessage"] = "Đã xóa công việc khỏi hệ thống! 🗑️";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Hàm nội bộ kiểm tra sự tồn tại của Công việc theo khóa chính
        /// </summary>
        private bool CongViecExists(int id)
        {
            return _context.CongViecs.Any(e => e.Id == id);
        }

        // =========================================================================
        // TÍNH NĂNG MỞ RỘNG: LẤY CÔNG VIỆC CẬN HẠN (< 5 PHÚT) & XỬ LÝ CHATBOT THÔNG MINH
        // =========================================================================

        /// <summary>
        /// GET: CongViec/GetUpcomingDeadlines
        /// API thực thi cơ chế Polling chạy ngầm, quét dữ liệu tìm tác vụ sắp hết hạn dưới 5 phút để reo chuông báo thức.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetUpcomingDeadlines()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Json(new List<object>());

            var now = DateTime.Now;
            var limitTime = now.AddMinutes(5);

            // Truy vấn lấy dữ liệu rút gọn tối ưu hiệu năng mạng cho API ngầm
            var upcomingTasks = await _context.CongViecs
                .Where(c => c.UserId == user.Id && !c.DaHoanThanh && c.NgayHetHan > now && c.NgayHetHan <= limitTime)
                .Select(c => new { id = c.Id, ten = c.TenCongViec, han = c.NgayHetHan.ToString("HH:mm") })
                .ToListAsync();

            return Json(upcomingTasks);
        }

        /// <summary>
        /// POST: CongViec/AskChatbot
        /// API xử lý ngôn ngữ tự nhiên cơ bản tích hợp hệ thống, đọc trực tiếp dữ liệu Real-time để trả lời người dùng.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> AskChatbot(string message)
        {
            if (string.IsNullOrEmpty(message))
                return Json(new { reply = "Chào bạn! Bạn có câu hỏi nào cần Trợ lý ToDoTech giải đáp không ạ? 🤖" });

            var user = await GetCurrentUserAsync();
            string userId = user?.Id ?? "";

            // Thống kê động thời gian thực phục vụ cho các câu hỏi tổng hợp dữ liệu cá nhân
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

            // Thuật toán bóc tách từ khóa tự nhiên ánh xạ kịch bản phản hồi thông minh
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
                        $"2. **Sinh việc tự động:** Cơ chế bốc ngẫu nhiên việc đa ngành thực tế (Y tế, Giáo dục, Văn phòng, Marketing...).\n" +
                        $"3. **Cảnh báo chuông reo độc quyền:** Kỹ thuật Polling ngầm phối hợp **Web Audio API** reo chuông báo thức báo động khi việc sắp hết hạn dưới 5 phút.\n" +
                        $"4. **Chatbot AI nội bộ:** Đọc hiểu số liệu hệ thống thời gian thực, tư vấn và phản hồi nhanh.\n" +
                        $"5. **An ninh bảo mật tuyệt đối:** Gửi mã xác thực OTP 6 số qua Email khi đăng ký và liên kết chống trùng lặp tài khoản bằng Google Auth.";
            }
            else if (input.Contains("chuông") || input.Contains("thông báo") || input.Contains("âm thanh") || input.Contains("5 phút"))
            {
                reply = "🔔 **Cơ chế chuông báo thức:** Hệ thống định kỳ gửi gói tin quét cơ sở dữ liệu ngầm. Khi phát hiện bất kỳ công việc nào chưa làm có hạn chót còn dưới 5 phút, trình duyệt nhận diện và gọi lệnh kích hoạt âm thanh chuông reo cảnh báo sinh động, đi kèm hộp Toast nhấp nháy.";
            }
            else if (input.Contains("ảnh đại diện") || input.Contains("avatar") || input.Contains("tải ảnh"))
            {
                reply = "🖼️ **Cách tải nhiều ảnh đại diện (Multiple Upload):** Bạn di chuyển đến khu vực **'Hồ sơ cá nhân'**, bấm nút chọn File. Hệ thống cho phép quét chuột chọn **hàng loạt hình ảnh cùng một lúc**. Danh sách đường dẫn ảnh sẽ được chuỗi hóa ngăn cách nhau bằng dấu chấm phẩy (`;`) để lưu trữ tối ưu trong DB.";
            }
            else if (input.Contains("google") || input.Contains("đăng nhập"))
            {
                reply = "🔐 **Đăng nhập tích hợp Google Auth:** Khi chọn đăng nhập thông qua Google, hệ thống tự động kiểm tra Email của bạn. Nếu Email đã tồn tại do đăng ký thủ công trước đó, cơ chế thông minh sẽ tự động gọi phương thức `AddLoginAsync` của Identity để liên kết tài khoản ngay lập tức mà không gây lỗi khóa ngoại.";
            }
            else if (input.Contains("chào") || input.Contains("hi") || input.Contains("hello"))
            {
                reply = "👋 Xin chào! Tôi là Trợ lý ảo thông minh của hệ thống ToDoTech. Tôi có thể giúp gì cho bạn trong việc quản lý công việc và tìm hiểu hệ thống hôm nay?";
            }
            else
            {
                // Phản hồi dự phòng tự nhiên cho các câu hỏi nằm ngoài luồng kịch bản mẫu
                reply = $"🤖 **Trợ lý ảo ghi nhận câu hỏi của bạn:** *\"{message}\"*\n\n" +
                        $"Hệ thống ToDoTech hiện hỗ trợ xuất sắc các nghiệp vụ: Lên lịch, reo chuông cảnh báo (<5 phút), Chatbot đọc số liệu thực, Đăng nhập Google Auth và Upload đa ảnh đại diện. Nếu cần trợ giúp sâu về thuật toán, bạn hãy liên hệ trực tiếp các thành viên Nhóm 6 nhé!";
            }

            return Json(new { reply });
        }
    }
}