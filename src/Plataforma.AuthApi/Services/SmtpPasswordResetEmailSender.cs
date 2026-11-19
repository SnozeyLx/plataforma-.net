using System.Security.Authentication;
using System.Text.Encodings.Web;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;
using Plataforma.AuthApi.Configuration;

namespace Plataforma.AuthApi.Services;

public sealed class SmtpPasswordResetEmailSender(
    IOptions<EmailOptions> configuredOptions) : IPasswordResetEmailSender
{
    private readonly EmailOptions _options = configuredOptions.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task SendPasswordResetAsync(
        string recipient,
        string resetUrl,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("SMTP email delivery is not configured.");
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = "Redefinição da senha da Plataforma";

        var encodedUrl = HtmlEncoder.Default.Encode(resetUrl);
        message.Body = new BodyBuilder
        {
            TextBody =
                $"Recebemos uma solicitação para redefinir a senha da sua conta.\n\n" +
                $"Acesse este link para escolher uma nova senha:\n{resetUrl}\n\n" +
                "Se você não solicitou a redefinição, ignore esta mensagem.",
            HtmlBody =
                "<p>Recebemos uma solicitação para redefinir a senha da sua conta.</p>" +
                $"<p><a href=\"{encodedUrl}\">Definir nova senha</a></p>" +
                "<p>Se você não solicitou a redefinição, ignore esta mensagem.</p>"
        }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(
            _options.Host,
            _options.Port,
            SecureSocketOptions.StartTls,
            cancellationToken);
        await client.AuthenticateAsync(
            _options.Username,
            _options.Password,
            cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
