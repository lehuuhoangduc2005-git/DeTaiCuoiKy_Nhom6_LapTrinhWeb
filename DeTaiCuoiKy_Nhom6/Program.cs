using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using DeTaiCuoiKy_Nhom6.Data;
using DeTaiCuoiKy_Nhom6.Models;
using DeTaiCuoiKy_Nhom6.Services;

var builder = WebApplication.CreateBuilder(args);

// Đăng ký dịch vụ gửi Mail thông báo
builder.Services.AddTransient<EmailSender>();

// Kết nối cơ sở dữ liệu SQL Server (Lấy chuỗi kết nối từ appsettings.json)
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 1. Cấu hình Identity quản lý tài khoản & phân quyền
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options => {
    options.SignIn.RequireConfirmedAccount = false; // Tắt xác thực Email để không dính lỗi 404 khi đăng ký
    options.Password.RequireDigit = false;          // Không bắt buộc mật khẩu chứa số
    options.Password.RequiredLength = 4;            // Độ dài mật khẩu tối thiểu 4 ký tự
    options.Password.RequireNonAlphanumeric = false;// Không bắt buộc ký tự đặc biệt
    options.Password.RequireUppercase = false;      // Không bắt buộc chữ viết hoa
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// Cấu hình đường dẫn Cookie để tự động định tuyến về các trang của bạn
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

// 2. Cấu hình Đăng nhập bằng tài khoản Google cá nhân
builder.Services.AddAuthentication()
    .AddGoogle(googleOptions =>
    {
        // Tự động quét lấy bộ khóa từ file appsettings.json
        googleOptions.ClientId = builder.Configuration["Authentication:Google:ClientId"];
        googleOptions.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];

        // 🔥 KHẮC PHỤC LỖI BẤM HỦY: Bắt sự kiện lỗi từ xa (Remote Failure)
        googleOptions.Events = new Microsoft.AspNetCore.Authentication.OAuth.OAuthEvents
        {
            OnRemoteFailure = context =>
            {
                // Khi người dùng bấm Hủy hoặc đóng tab chọn tài khoản Google
                // Chuyển hướng êm đẹp về lại trang đăng nhập gốc
                context.Response.Redirect("/Account/Login");

                // Đánh dấu là lỗi đã được xử lý xong, KHÔNG ném ra màn hình crash (Unhandled Exception) nữa
                context.HandleResponse();

                return Task.CompletedTask;
            }
        };
    });

// Kích hoạt dịch vụ MVC (Controller & Views)
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Cấu hình Pipeline xử lý các Requests HTTP
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// ⚠️ Thứ tự bắt buộc: Xác thực danh tính trước, phân quyền sau
app.UseAuthentication();
app.UseAuthorization();

// Định tuyến mặc định dẫn vào trang Quản lý Công Việc của nhóm bạn
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=CongViec}/{action=Index}/{id?}");

app.Run();