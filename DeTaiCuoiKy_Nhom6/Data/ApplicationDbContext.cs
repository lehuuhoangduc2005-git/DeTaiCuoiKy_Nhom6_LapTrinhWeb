using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using DeTaiCuoiKy_Nhom6.Models;

namespace DeTaiCuoiKy_Nhom6.Data
{
    // Bắt buộc đổi sang kế thừa IdentityDbContext để quản lý User/Role
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<CongViec> CongViecs { get; set; }
    }
}