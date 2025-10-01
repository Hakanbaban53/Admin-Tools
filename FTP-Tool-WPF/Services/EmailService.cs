using FTP_Tool.Models;
using MailKit.Security;
using MimeKit;

namespace FTP_Tool.Services
{
    public class EmailService
    {
        private readonly AppSettings _settings;
        private readonly CredentialService _credentialService;

        public EmailService(AppSettings settings, CredentialService credentialService)
        {
            _settings = settings;
            _credentialService = credentialService;
        }

        private (string user, string pass) GetSmtpCredentials()
        {
            var user = _settings.SmtpUsername ?? string.Empty;
            var pass = string.Empty;
            try
            {
                // Load SMTP credentials from credential store. The app saves SMTP creds using category "smtp",
                // so ensure we request that category here instead of the default (which is "ftp").
                var cred = _credentialService.Load(_settings.SmtpHost ?? string.Empty, _settings.SmtpUsername ?? string.Empty, "smtp");
                if (cred.HasValue)
                {
                    // Use the stored username if settings do not contain one
                    if (string.IsNullOrEmpty(user)) user = cred.Value.Username ?? string.Empty;
                    pass = cred.Value.Password ?? string.Empty;
                }
            }
            catch { }
            return (user, pass);
        }

        public async Task SendEmailAsync(string subject, string body)
        {
            if (string.IsNullOrWhiteSpace(_settings.SmtpHost)) throw new InvalidOperationException("SMTP host not configured");
            if (string.IsNullOrWhiteSpace(_settings.EmailFrom)) throw new InvalidOperationException("From address not configured");
            if (string.IsNullOrWhiteSpace(_settings.EmailRecipients)) throw new InvalidOperationException("No recipients configured");

            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(_settings.EmailFrom));

            var recipients = _settings.EmailRecipients.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim()).Where(r => !string.IsNullOrEmpty(r));
            foreach (var r in recipients)
            {
                try { message.To.Add(MailboxAddress.Parse(r)); } catch { }
            }

            message.Subject = subject ?? string.Empty;
            message.Body = new TextPart("plain") { Text = body ?? string.Empty };

            var (user, pass) = GetSmtpCredentials();

            using var client = new MailKit.Net.Smtp.SmtpClient();
            try
            {
                client.CheckCertificateRevocation = false;
                var useSsl = _settings.SmtpEnableSsl;
                if (useSsl)
                {
                    await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, SecureSocketOptions.StartTlsWhenAvailable).ConfigureAwait(false);
                }
                else
                {
                    await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, SecureSocketOptions.Auto).ConfigureAwait(false);
                }

                // Only attempt authentication if we have both username and password
                if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(pass))
                {
                    await client.AuthenticateAsync(user, pass).ConfigureAwait(false);
                }

                await client.SendAsync(message).ConfigureAwait(false);
            }
            finally
            {
                try { await client.DisconnectAsync(true).ConfigureAwait(false); } catch { }
                client.Dispose();
            }
        }

        public Task SendTestEmailAsync() => SendEmailAsync("FTP Monitor - Test", "This is a test email from FTP Monitor.");
    }
}
