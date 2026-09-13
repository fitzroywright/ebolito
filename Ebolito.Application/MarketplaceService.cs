using Ebolito.Domain;

namespace Ebolito.Application;

public sealed record ProfessionalCard(
    Guid Id,
    string Slug,
    string DisplayName,
    string Headline,
    string Location,
    double Rating,
    int ReviewCount,
    bool IsScreened);

public sealed record ProfessionalProfile(
    Professional Professional,
    IReadOnlyCollection<Skill> Skills,
    IReadOnlyCollection<PortfolioProject> Projects,
    IReadOnlyCollection<Review> Reviews,
    double Rating);

public sealed record EngagementRequest(
    Guid ProfessionalId,
    Guid CustomerId,
    Guid? SkillId,
    string RequestText,
    string Location,
    EngagementChannel CustomerPreferredContactChannel = EngagementChannel.WhatsApp);

public enum EngagementResponse { Accept, Decline }

public interface IMarketplaceStore
{
    Task<IReadOnlyCollection<Skill>> GetSkillsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Professional>> GetProfessionalsAsync(CancellationToken cancellationToken = default);
    Task<Professional?> GetProfessionalAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Professional?> GetProfessionalBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<PortfolioProject>> GetProjectsAsync(Guid professionalId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Review>> GetReviewsAsync(Guid professionalId, CancellationToken cancellationToken = default);
    Task<CustomerIdentity?> GetCustomerAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CustomerIdentity?> GetCustomerByMobileAsync(string mobileNumber, CancellationToken cancellationToken = default);
    Task SaveCustomerAsync(CustomerIdentity customer, CancellationToken cancellationToken = default);
    Task SaveEngagementAsync(Engagement engagement, CancellationToken cancellationToken = default);
    Task<Engagement?> GetEngagementAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Engagement>> GetUnacknowledgedEngagementsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);
    Task<ProfessionalNotificationPolicy> GetNotificationPolicyAsync(Guid professionalId, CancellationToken cancellationToken = default);
    Task SaveDeliveryAttemptAsync(EngagementDeliveryAttempt attempt, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<EngagementDeliveryAttempt>> GetDeliveryAttemptsAsync(Guid engagementId, CancellationToken cancellationToken = default);
}

public interface IEngagementNotifier
{
    Task<EngagementChannel> DeliverAsync(
        Professional professional,
        CustomerIdentity customer,
        Engagement engagement,
        ProfessionalNotificationPolicy policy,
        IReadOnlyCollection<EngagementDeliveryAttempt> previousAttempts,
        CancellationToken cancellationToken = default);

    Task NotifyCustomerAsync(CustomerIdentity customer, Professional professional, Engagement engagement, CancellationToken cancellationToken = default);
}

public interface IMarketplaceService
{
    Task<IReadOnlyCollection<ProfessionalCard>> SearchAsync(string? service, string? location, CancellationToken cancellationToken = default);
    Task<ProfessionalProfile?> GetProfileAsync(string slug, CancellationToken cancellationToken = default);
    Task<Engagement> RequestEngagementAsync(EngagementRequest request, CancellationToken cancellationToken = default);
    Task<Engagement> RespondToEngagementAsync(Guid engagementId, EngagementResponse response, CancellationToken cancellationToken = default);
    Task<bool> EscalateEngagementAsync(Guid engagementId, CancellationToken cancellationToken = default);
}

public sealed class MarketplaceService(IMarketplaceStore store, IEngagementNotifier notifier) : IMarketplaceService
{
    public async Task<IReadOnlyCollection<ProfessionalCard>> SearchAsync(string? service, string? location, CancellationToken cancellationToken = default)
    {
        var professionals = await store.GetProfessionalsAsync(cancellationToken);
        var skills = await store.GetSkillsAsync(cancellationToken);
        var skillLookup = skills.ToDictionary(x => x.Id);
        var serviceTerm = service?.Trim();
        var locationTerm = location?.Trim();

        var results = new List<ProfessionalCard>();
        foreach (var professional in professionals.Where(x => x.IsActive))
        {
            if (!string.IsNullOrWhiteSpace(serviceTerm))
            {
                var matchesSkill = professional.SkillIds
                    .Where(skillLookup.ContainsKey)
                    .Select(id => skillLookup[id])
                    .Any(skill => skill.Name.Contains(serviceTerm, StringComparison.OrdinalIgnoreCase)
                        || skill.Synonyms.Any(s => s.Contains(serviceTerm, StringComparison.OrdinalIgnoreCase)));
                if (!matchesSkill) continue;
            }

            if (!string.IsNullOrWhiteSpace(locationTerm)
                && !professional.ServiceAreas.Any(area => area.ToString().Contains(locationTerm, StringComparison.OrdinalIgnoreCase)))
                continue;

            var reviews = await store.GetReviewsAsync(professional.Id, cancellationToken);
            results.Add(new ProfessionalCard(
                professional.Id,
                professional.Slug,
                professional.DisplayName,
                professional.Headline,
                professional.ServiceAreas.FirstOrDefault()?.ToString() ?? "Jamaica",
                reviews.Count == 0 ? 0 : Math.Round(reviews.Average(x => x.Rating), 1),
                reviews.Count,
                professional.IsScreened));
        }

        return results
            .OrderByDescending(x => x.IsScreened)
            .ThenByDescending(x => x.Rating)
            .ThenBy(x => x.DisplayName)
            .ToArray();
    }

    public async Task<ProfessionalProfile?> GetProfileAsync(string slug, CancellationToken cancellationToken = default)
    {
        var professional = await store.GetProfessionalBySlugAsync(slug, cancellationToken);
        if (professional is null || !professional.IsActive) return null;

        var skills = (await store.GetSkillsAsync(cancellationToken)).Where(x => professional.SkillIds.Contains(x.Id)).ToArray();
        var projects = await store.GetProjectsAsync(professional.Id, cancellationToken);
        var reviews = await store.GetReviewsAsync(professional.Id, cancellationToken);
        var rating = reviews.Count == 0 ? 0 : Math.Round(reviews.Average(x => x.Rating), 1);
        return new ProfessionalProfile(professional, skills, projects, reviews, rating);
    }

    public async Task<Engagement> RequestEngagementAsync(EngagementRequest request, CancellationToken cancellationToken = default)
    {
        var professional = await store.GetProfessionalAsync(request.ProfessionalId, cancellationToken)
            ?? throw new InvalidOperationException("Professional not found.");
        var customer = await store.GetCustomerAsync(request.CustomerId, cancellationToken)
            ?? throw new InvalidOperationException("Verified customer identity is required before an engagement can be sent.");

        if (string.IsNullOrWhiteSpace(request.RequestText)) throw new ArgumentException("Request text is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Location)) throw new ArgumentException("Location is required.", nameof(request));

        var engagement = new Engagement
        {
            ProfessionalId = professional.Id,
            CustomerId = customer.Id,
            SkillId = request.SkillId,
            RequestText = request.RequestText.Trim(),
            Location = request.Location.Trim(),
            RequestedChannel = request.CustomerPreferredContactChannel
        };

        await store.SaveEngagementAsync(engagement, cancellationToken);
        await DeliverAndRecordAsync(professional, customer, engagement, cancellationToken);
        return engagement;
    }

    public async Task<Engagement> RespondToEngagementAsync(Guid engagementId, EngagementResponse response, CancellationToken cancellationToken = default)
    {
        var engagement = await store.GetEngagementAsync(engagementId, cancellationToken)
            ?? throw new InvalidOperationException("Engagement not found.");
        var professional = await store.GetProfessionalAsync(engagement.ProfessionalId, cancellationToken)
            ?? throw new InvalidOperationException("Professional not found.");
        var customer = await store.GetCustomerAsync(engagement.CustomerId, cancellationToken)
            ?? throw new InvalidOperationException("Customer not found.");

        if (response == EngagementResponse.Accept) engagement.Accept();
        else engagement.Decline();

        await store.SaveEngagementAsync(engagement, cancellationToken);
        await notifier.NotifyCustomerAsync(customer, professional, engagement, cancellationToken);
        return engagement;
    }

    public async Task<bool> EscalateEngagementAsync(Guid engagementId, CancellationToken cancellationToken = default)
    {
        var engagement = await store.GetEngagementAsync(engagementId, cancellationToken);
        if (engagement is null || engagement.Status != EngagementStatus.Delivered) return false;

        var professional = await store.GetProfessionalAsync(engagement.ProfessionalId, cancellationToken);
        var customer = await store.GetCustomerAsync(engagement.CustomerId, cancellationToken);
        if (professional is null || customer is null) return false;

        var before = engagement.DeliveredChannel;
        await DeliverAndRecordAsync(professional, customer, engagement, cancellationToken);
        return engagement.DeliveredChannel != before;
    }

    private async Task DeliverAndRecordAsync(Professional professional, CustomerIdentity customer, Engagement engagement, CancellationToken cancellationToken)
    {
        var policy = await store.GetNotificationPolicyAsync(professional.Id, cancellationToken);
        var attempts = await store.GetDeliveryAttemptsAsync(engagement.Id, cancellationToken);
        var deliveredChannel = await notifier.DeliverAsync(professional, customer, engagement, policy, attempts, cancellationToken);

        await store.SaveDeliveryAttemptAsync(new EngagementDeliveryAttempt(
            Guid.NewGuid(), engagement.Id, deliveredChannel, DateTimeOffset.UtcNow, true, "Delivered by configured channel router."), cancellationToken);

        engagement.RecordDelivery(deliveredChannel);
        await store.SaveEngagementAsync(engagement, cancellationToken);
    }
}
