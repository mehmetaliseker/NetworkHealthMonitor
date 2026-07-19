using System.Net;
using System.Net.Mail;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class SmtpEmailSender : IEmailSender
{
    public Task<NotificationSendResult> TestConnectionAsync(
        NotificationSettings settings,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateSettings(settings, requireRecipient: false);
        return Task.FromResult(validation ?? NotificationSendResult.Ok());
    }

    public async Task<NotificationSendResult> SendAsync(
        NotificationSettings settings,
        EmailRecipient recipient,
        string subject,
        string body,
        bool isHtml,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateSettings(settings, requireRecipient: true, recipient);
        if (validation is not null)
        {
            return validation;
        }

        try
        {
            using var message = new MailMessage
            {
                From = string.IsNullOrWhiteSpace(settings.SenderDisplayName)
                    ? new MailAddress(settings.SenderEmail.Trim())
                    : new MailAddress(settings.SenderEmail.Trim(), settings.SenderDisplayName.Trim()),
                Subject = subject.Trim(),
                Body = body,
                IsBodyHtml = isHtml
            };
            message.To.Add(string.IsNullOrWhiteSpace(recipient.DisplayName)
                ? new MailAddress(recipient.Email.Trim())
                : new MailAddress(recipient.Email.Trim(), recipient.DisplayName.Trim()));

            using var client = new SmtpClient(settings.SmtpHost.Trim(), settings.SmtpPort)
            {
                Timeout = Math.Clamp(settings.SmtpConnectionTimeoutSeconds, 1, 300) * 1000,
                EnableSsl = settings.SmtpSecurity is SmtpSecurityMode.StartTls or SmtpSecurityMode.SslTls
            };

            if (!string.IsNullOrWhiteSpace(settings.SmtpUsername))
            {
                client.Credentials = new NetworkCredential(settings.SmtpUsername.Trim(), settings.SmtpPassword);
            }

            using var registration = cancellationToken.Register(client.SendAsyncCancel);
            await client.SendMailAsync(message, cancellationToken);
            return NotificationSendResult.Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SmtpException ex)
        {
            return IsTransient(ex)
                ? NotificationSendResult.TransientFailure(Sanitize(ex.Message))
                : NotificationSendResult.PermanentFailure(Sanitize(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return NotificationSendResult.PermanentFailure(Sanitize(ex.Message));
        }
        catch (FormatException ex)
        {
            return NotificationSendResult.PermanentFailure(Sanitize(ex.Message));
        }
    }

    private static NotificationSendResult? ValidateSettings(
        NotificationSettings settings,
        bool requireRecipient,
        EmailRecipient? recipient = null)
    {
        if (!settings.EmailEnabled && requireRecipient)
        {
            return NotificationSendResult.TransientFailure("E-posta bildirimleri kapali.");
        }

        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
        {
            return NotificationSendResult.PermanentFailure("SMTP sunucu adresi bos.");
        }

        if (settings.SmtpPort <= 0 || settings.SmtpPort > 65535)
        {
            return NotificationSendResult.PermanentFailure("SMTP portu gecersiz.");
        }

        if (settings.SmtpSecurity == SmtpSecurityMode.None && !settings.AllowInsecureSmtp)
        {
            return NotificationSendResult.PermanentFailure("Guvenliksiz SMTP baglantisi etkin; bilincli onay gerektirir.");
        }

        if (!EmailAddressValidator.IsValid(settings.SenderEmail))
        {
            return NotificationSendResult.PermanentFailure("Gonderen e-posta adresi gecersiz.");
        }

        if (requireRecipient && (recipient is null || !EmailAddressValidator.IsValid(recipient.Email)))
        {
            return NotificationSendResult.PermanentFailure("Alici e-posta adresi gecersiz.");
        }

        return null;
    }

    private static bool IsTransient(SmtpException ex)
    {
        return ex.StatusCode is SmtpStatusCode.GeneralFailure
            or SmtpStatusCode.LocalErrorInProcessing
            or SmtpStatusCode.MailboxBusy
            or SmtpStatusCode.TransactionFailed
            or SmtpStatusCode.ServiceNotAvailable;
    }

    private static string SanitizedCredentialToken => "***";

    private static string Sanitize(string value)
    {
        var safe = (value ?? string.Empty).ReplaceLineEndings(" ").Trim();
        return string.IsNullOrWhiteSpace(safe) ? SanitizedCredentialToken : safe;
    }
}
