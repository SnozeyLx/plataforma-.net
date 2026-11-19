namespace Plataforma.AuthApi.Services;

public interface IPasswordResetEmailSender
{
    bool IsConfigured { get; }

    Task SendPasswordResetAsync(
        string recipient,
        string resetUrl,
        CancellationToken cancellationToken);
}
