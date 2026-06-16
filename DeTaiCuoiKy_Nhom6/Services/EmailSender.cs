using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

namespace DeTaiCuoiKy_Nhom6.Services
{
    public class EmailSender
    {
        private readonly IConfiguration _configuration;

        public EmailSender(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task SendVerificationCodeAsync(string email, string code)
        {
            // Tự động lấy cấu hình an toàn từ appsettings.json
            var smtpServer = _configuration["EmailSettings:SmtpServer"] ?? "smtp.gmail.com";
            var port = int.Parse(_configuration["EmailSettings:Port"] ?? "587");
            var senderEmail = _configuration["EmailSettings:SenderEmail"] ?? "leduc7393@gmail.com";
            var senderName = _configuration["EmailSettings:SenderName"] ?? "HỆ THỐNG QUẢN LÝ CÔNG VIỆC";
            var password = _configuration["EmailSettings:Password"];

            using var smtpClient = new SmtpClient(smtpServer)
            {
                Port = port,
                Credentials = new NetworkCredential(senderEmail, password),
                EnableSsl = true,
            };

            using var mailMessage = new MailMessage
            {
                From = new MailAddress(senderEmail, senderName),
                Subject = "[Đồ Án Nhóm 6] - Mã Xác Minh Kích Hoạt Tài Khoản",
                Body = $@"
                    <div style='font-family: Arial, sans-serif; padding: 20px; border: 1px solid #eee; max-width: 600px; border-radius: 12px; margin: 0 auto;'>
                        <h2 style='color: #198754; text-align: center; margin-bottom: 20px;'>CHÀO MỪNG THÀNH VIÊN MỚI!</h2>
                        <p>Cảm ơn bạn đã đăng ký tài khoản tại hệ thống quản lý của chúng tôi.</p>
                        <p>Vui lòng sử dụng mã xác minh dưới đây để hoàn tất kích hoạt tài khoản:</p>
                        <div style='text-align: center; margin: 30px 0;'>
                            <span style='font-size: 32px; font-weight: bold; letter-spacing: 5px; color: #dc3545; background: #f8f9fa; padding: 12px 25px; border: 2px dashed #dc3545; border-radius: 8px; display: inline-block;'>{code}</span>
                        </div>
                        <p style='color: #6c757d; font-size: 12px; border-top: 1px solid #eee; padding-top: 15px; margin-top: 20px;'>Mã này có hiệu lực trong vòng 10 phút. Tuyệt đối không chia sẻ mã này cho bất kỳ ai để bảo mật tài khoản.</p>
                    </div>",
                IsBodyHtml = true,
            };

            mailMessage.To.Add(email);
            await smtpClient.SendMailAsync(mailMessage);
        }
    }
}