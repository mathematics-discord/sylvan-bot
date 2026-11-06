namespace SylvanBot.Features.SylvanMail;

public sealed class SylvanMailHostedService(SylvanMailClient mail, SylvanMailGatewayHandler handler) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => mail.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
    {
        handler.StopMailAdmission();

        return mail.StopAsync(cancellationToken);
    }
}
