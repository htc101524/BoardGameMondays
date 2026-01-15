using System.Threading.Tasks;

namespace BoardGameMondays.Core;

public interface IEmailSender
{
    Task SendEmailAsync(string toEmail, string subject, string htmlBody);
}
