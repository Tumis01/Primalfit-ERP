using MailKit.Net.Smtp;
using MimeKit;

namespace Primafit_ERP.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(string recipientEmail, string subject, string body, byte[] attachment = null, string fileName = null);
    }

    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        public async Task SendEmailAsync(string recipientEmail, string subject, string body, byte[] attachment = null, string fileName = null)
        {
            var email = new MimeMessage();
            email.From.Add(new MailboxAddress(_config["EmailSettings:SenderName"], _config["EmailSettings:SenderEmail"]));
            email.To.Add(MailboxAddress.Parse(recipientEmail));
            email.Subject = subject;

            var builder = new BodyBuilder { HtmlBody = body };

            if (attachment != null)
            {
                builder.Attachments.Add(fileName ?? "Document.pdf", attachment);
            }

            email.Body = builder.ToMessageBody();

            using var smtp = new SmtpClient();
            // Connect via STARTTLS
            await smtp.ConnectAsync(_config["EmailSettings:SmtpServer"],
                                    int.Parse(_config["EmailSettings:SmtpPort"]),
                                    MailKit.Security.SecureSocketOptions.StartTls);

            await smtp.AuthenticateAsync(_config["EmailSettings:SenderEmail"], _config["EmailSettings:AppPassword"]);
            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);
        }
    }
}
