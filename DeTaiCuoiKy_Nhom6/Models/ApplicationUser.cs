using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;

namespace DeTaiCuoiKy_Nhom6.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string HoTen { get; set; } = string.Empty;

        // Tính năng Gamification
        public int DiemXP { get; set; } = 0;
        public int CapDo => (DiemXP / 100) + 1; // Cứ 100 XP là lên 1 cấp

        /// <summary>
        /// Đánh giá mức độ tích cực của thành viên dựa trên điểm số XP thực tế
        /// </summary>
        public string DanhHieu
        {
            get
            {
                if (DiemXP >= 500) return "Xuất sắc & Uy tín 🏆";
                if (DiemXP >= 300) return "Năng nổ & Tích cực ⚡";
                if (DiemXP >= 100) return "Bình thường 😐";
                if (DiemXP >= 30) return "Lười biếng 🦥";
                return "Kém uy tín ⚠️";
            }
        }

        // Tính năng chọn 1 hoặc nhiều ảnh làm bộ sưu tập Avatar
        public virtual ICollection<UserAvatar> BoSuuTapAvatar { get; set; } = new List<UserAvatar>();
        public string? Avatars { get; set; }
    }

    public class UserAvatar
    {
        public int Id { get; set; }
        public string DuongDanAnh { get; set; } = string.Empty;
        public bool DangSuDung { get; set; } = false; // Đánh dấu ảnh nào đang làm avatar chính
        public string UserId { get; set; } = string.Empty;
        public virtual ApplicationUser User { get; set; } = null!;
    }
}