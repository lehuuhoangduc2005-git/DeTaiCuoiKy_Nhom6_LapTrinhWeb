using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeTaiCuoiKy_Nhom6.Models
{
    /// <summary>
    /// Model đại diện cho cấu trúc bảng Công việc (CongViecs) trong cơ sở dữ liệu SQL Server.
    /// Quản lý chi tiết tiến độ, phân loại ngành nghề, phân bổ ma trận Eisenhower và định danh người thực hiện/người tạo.
    /// </summary>
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
        [DataType(DataType.DateTime)] // Sử dụng cấu hình DateTime để lưu vết đầy đủ cả ngày, giờ, phút phục vụ hệ thống reo chuông báo động
        public DateTime NgayHetHan { get; set; }

        [Display(Name = "Trạng thái")]
        public bool DaHoanThanh { get; set; } = false;

        [Required]
        [Display(Name = "Phân loại")]
        public string PhanLoai { get; set; } = "Cá nhân"; // Danh mục mặc định. Các lựa chọn khác: Học tập, Công việc, Văn phòng, Y tế, Giáo dục...

        [Display(Name = "Vị trí Ma trận Eisenhower")]
        public int MaTranEisenhower { get; set; } = 4; // Quy ước mặc định: Ô số 4 (Không gấp - Không quan trọng)

        /// <summary>
        /// ID của tài khoản đang chịu trách nhiệm thực hiện công việc này.
        /// Dùng để truy vấn lọc danh sách hiển thị trên trang Index của riêng user đó.
        /// </summary>
        [Display(Name = "Người nhận việc")]
        public string? UserId { get; set; }

        /// <summary>
        /// Thuộc tính điều hướng liên kết ngoại (Navigation Property) kết nối trực tiếp đến bảng tài khoản Identity hệ thống.
        /// </summary>
        [ForeignKey("UserId")]
        public virtual ApplicationUser? User { get; set; }

        /// <summary>
        /// Dùng để lưu lại ID của tài khoản thực tế đã thao tác nhấn nút tạo ra bản ghi này.
        /// Phục vụ minh bạch nghiệp vụ khi Admin/Trưởng nhóm tạo việc rồi bàn giao (Assign) cho User cấp dưới.
        /// </summary>
        [Display(Name = "Người tạo thực tế")]
        public string? CreatedByUserId { get; set; }
    }
}