using Ebolito.Domain.Engagements;

namespace Ebolito.Application.Engagements;

public sealed record CreateEngagementRequest(
    Guid CustomerId,
    Guid ProfessionalId,
    string Service,
    string? Message,
    string? PreferredContactChannel);

public interface IEngagementRepository
{
    Task AddAsync(Engagement engagement, CancellationToken cancellationToken = default);
    Task<Engagement?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveAsync(Engagement engagement, CancellationToken cancellationToken = default);
}

public interface IEngagementNotifier
{
    Task NotifyRequestedAsync(Engagement engagement, CancellationToken cancellationToken = default);
    Task NotifyStatusChangedAsync(Engagement engagement, CancellationToken cancellationToken = default);
}

public sealed class EngagementService(IEngagementRepository repository, IEngagementNotifier notifier)
{
    public async Task<Engagement> CreateAsync(CreateEngagementRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Service);
        var engagement = new Engagement
        {
            CustomerId = request.CustomerId,
            ProfessionalId = request.ProfessionalId,
            Service = request.Service.Trim(),
            Message = request.Message?.Trim(),
            PreferredContactChannel = request.PreferredContactChannel?.Trim()
        };

        await repository.AddAsync(engagement, cancellationToken);
        await notifier.NotifyRequestedAsync(engagement, cancellationToken);
        return engagement;
    }

    public async Task RespondAsync(Guid id, bool accept, CancellationToken cancellationToken = default)
    {
        var engagement = await repository.GetAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Engagement {id} was not found.");

        if (accept) engagement.Accept(DateTimeOffset.UtcNow); else engagement.Decline(DateTimeOffset.UtcNow);
        await repository.SaveAsync(engagement, cancellationToken);
        await notifier.NotifyStatusChangedAsync(engagement, cancellationToken);
    }
}