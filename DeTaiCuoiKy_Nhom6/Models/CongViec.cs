using System;
using System.ComponentModel.DataAnnotations;

namespace DeTaiCuoiKy_Nhom6.Models
{
    public class CongViec
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "Tên công việc không được để trống")]
        [Display(Name = "Tên công việc")]
        public string TenCongViec { get; set; }

        [Display(Name = "Mô tả")]
        public string? MoTa { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn ngày hết hạn")]
        [Display(Name = "Ngày hết hạn")]
        [DataType(DataType.DateTime)] // Đã chuyển từ DataType.Date sang DataType.DateTime để lưu đầy đủ cả giờ và phút cho chuông báo
        public DateTime NgayHetHan { get; set; }

        [Display(Name = "Trạng thái")]
        public bool DaHoanThanh { get; set; } = false;

        [Required]
        [Display(Name = "Phân loại")]
        public string PhanLoai { get; set; } = "Cá nhân"; // Học tập, Công việc, Giải trí...

        public int MaTranEisenhower { get; set; } = 4; // Mặc định là Ô số 4 (Không gấp - Không quan trọng)

        // ID của người được giao/sở hữu công việc này để hiển thị trên Index của họ
        public string? UserId { get; set; }
        public virtual ApplicationUser? User { get; set; }

        // Dùng để lưu lại ID của tài khoản thực tế đã tạo ra bản ghi này (Admin hoặc chính User)
        [Display(Name = "Người tạo thực tế")]
        public string? CreatedByUserId { get; set; }
    }
}