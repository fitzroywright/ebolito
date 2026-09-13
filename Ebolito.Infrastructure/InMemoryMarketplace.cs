using System.Collections.Concurrent;
using Ebolito.Application;
using Ebolito.Domain;

namespace Ebolito.Infrastructure;

public sealed class InMemoryMarketplaceStore : IMarketplaceStore
{
    private readonly IReadOnlyCollection<Skill> _skills;
    private readonly ConcurrentDictionary<Guid, Professional> _professionals = new();
    private readonly ConcurrentDictionary<Guid, PortfolioProject> _projects = new();
    private readonly ConcurrentDictionary<Guid, Review> _reviews = new();
    private readonly ConcurrentDictionary<Guid, CustomerIdentity> _customers = new();
    private readonly ConcurrentDictionary<Guid, Engagement> _engagements = new();
    private readonly ConcurrentDictionary<Guid, ProfessionalNotificationPolicy> _notificationPolicies = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<EngagementDeliveryAttempt>> _deliveryAttempts = new();

    public InMemoryMarketplaceStore()
    {
        var plumbing = new Skill(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Plumbing", ["plumber", "pipe repair", "leak"]);
        var carpentry = new Skill(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Carpentry", ["carpenter", "cabinetry", "woodwork"]);
        var electrical = new Skill(Guid.Parse("33333333-3333-3333-3333-333333333333"), "Electrical", ["electrician", "wiring", "electrical installation"]);
        _skills = [plumbing, carpentry, electrical];
        var beverlyId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var marcusId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        _professionals[beverlyId] = new Professional(beverlyId, "beverly-hyman", "Beverly Hyman", "Hyman Plumbing Services", "Reliable plumbing for homes and businesses", "Experienced plumber serving Kingston and St. Andrew. Repairs, installations and emergency work.", "+18765550101", "+18765550101", [plumbing.Id], [new ServiceArea("Kingston"), new ServiceArea("St. Andrew")], true);
        _professionals[marcusId] = new Professional(marcusId, "marcus-brown", "Marcus Brown", "Brown Custom Woodwork", "Custom kitchens, cabinetry and furniture", "Custom carpentry focused on durable, practical work and clean finishes.", "+18765550102", "+18765550102", [carpentry.Id], [new ServiceArea("St. Catherine", "Spanish Town"), new ServiceArea("Kingston")], true);
        _notificationPolicies[beverlyId] = new ProfessionalNotificationPolicy(beverlyId, EngagementChannel.WhatsApp, null, EngagementChannel.Sms, TimeSpan.FromMinutes(10), [EngagementChannel.WhatsApp, EngagementChannel.Sms], [new NotificationEndpoint(EngagementChannel.WhatsApp, "+18765550101", "Beverly WhatsApp"), new NotificationEndpoint(EngagementChannel.Sms, "+18765550101", "Beverly mobile")]);
        _notificationPolicies[marcusId] = new ProfessionalNotificationPolicy(marcusId, EngagementChannel.Slack, EngagementChannel.Email, EngagementChannel.Sms, TimeSpan.FromMinutes(10), [EngagementChannel.Slack, EngagementChannel.Email, EngagementChannel.WhatsApp, EngagementChannel.Sms], [new NotificationEndpoint(EngagementChannel.Slack, "#new-leads", "Brown Custom Woodwork leads"), new NotificationEndpoint(EngagementChannel.Email, "leads@example.com", "Business email"), new NotificationEndpoint(EngagementChannel.WhatsApp, "+18765550102", "Marcus WhatsApp"), new NotificationEndpoint(EngagementChannel.Sms, "+18765550102", "Marcus mobile")]);
        var beverlyProject = new PortfolioProject(Guid.NewGuid(), beverlyId, "Kitchen Pipe Repair", "Replaced damaged supply lines and restored the kitchen sink installation.", "Kingston", DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-2)), [plumbing.Id], [new PortfolioPhoto(Guid.NewGuid(), "/images/demo/plumbing-1.jpg", "Completed kitchen plumbing")], true);
        var marcusProject = new PortfolioProject(Guid.NewGuid(), marcusId, "Modern Kitchen Cabinets", "Built and installed a full custom cabinet set with island storage.", "Spanish Town", DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-4)), [carpentry.Id], [new PortfolioPhoto(Guid.NewGuid(), "/images/demo/carpentry-1.jpg", "Custom kitchen cabinetry")], true);
        _projects[beverlyProject.Id] = beverlyProject; _projects[marcusProject.Id] = marcusProject;
        AddSeedReview(new Review(Guid.NewGuid(), beverlyId, null, "C. Wallace", 5, "Professional, on time and very knowledgeable.", DateTimeOffset.UtcNow.AddDays(-35), false));
        AddSeedReview(new Review(Guid.NewGuid(), marcusId, null, "A. Grant", 5, "Excellent workmanship and communication.", DateTimeOffset.UtcNow.AddDays(-18), false));
        var demoCustomer = new CustomerIdentity(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "Demo Customer", "+18765550999", "customer@example.com");
        _customers[demoCustomer.Id] = demoCustomer;
    }

    public Task<IReadOnlyCollection<Skill>> GetSkillsAsync(CancellationToken cancellationToken = default) => Task.FromResult(_skills);
    public Task<IReadOnlyCollection<Professional>> GetProfessionalsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<Professional>>(_professionals.Values.ToArray());
    public Task<Professional?> GetProfessionalAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_professionals.TryGetValue(id, out var value) ? value : null);
    public Task<Professional?> GetProfessionalBySlugAsync(string slug, CancellationToken cancellationToken = default) => Task.FromResult(_professionals.Values.FirstOrDefault(x => x.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase)));
    public Task SaveProfessionalAsync(Professional professional, CancellationToken cancellationToken = default) { _professionals[professional.Id] = professional; return Task.CompletedTask; }
    public Task<IReadOnlyCollection<PortfolioProject>> GetProjectsAsync(Guid professionalId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<PortfolioProject>>(_projects.Values.Where(x => x.ProfessionalId == professionalId).OrderByDescending(x => x.IsFeatured).ThenByDescending(x => x.CompletedOn).ToArray());
    public Task SavePortfolioProjectAsync(PortfolioProject project, CancellationToken cancellationToken = default) { _projects[project.Id] = project; return Task.CompletedTask; }
    public Task<bool> DeletePortfolioProjectAsync(Guid professionalId, Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(_projects.TryGetValue(projectId, out var project) && project.ProfessionalId == professionalId && _projects.TryRemove(projectId, out _));
    public Task<IReadOnlyCollection<Review>> GetReviewsAsync(Guid professionalId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<Review>>(_reviews.Values.Where(x => x.ProfessionalId == professionalId).OrderByDescending(x => x.CreatedAt).ToArray());
    public Task<Review?> GetReviewByEngagementAsync(Guid engagementId, CancellationToken cancellationToken = default) => Task.FromResult(_reviews.Values.FirstOrDefault(x => x.EngagementId == engagementId));
    public Task SaveReviewAsync(Review review, CancellationToken cancellationToken = default) { if (review.EngagementId is Guid engagementId && _reviews.Values.Any(x => x.EngagementId == engagementId && x.Id != review.Id)) throw new InvalidOperationException("This engagement has already been reviewed."); _reviews[review.Id] = review; return Task.CompletedTask; }
    public Task<CustomerIdentity?> GetCustomerAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_customers.TryGetValue(id, out var value) ? value : null);
    public Task<CustomerIdentity?> GetCustomerByMobileAsync(string mobileNumber, CancellationToken cancellationToken = default) => Task.FromResult(_customers.Values.FirstOrDefault(x => x.VerifiedMobileNumber.Equals(mobileNumber, StringComparison.OrdinalIgnoreCase)));
    public Task SaveCustomerAsync(CustomerIdentity customer, CancellationToken cancellationToken = default) { _customers[customer.Id] = customer; return Task.CompletedTask; }
    public Task SaveEngagementAsync(Engagement engagement, CancellationToken cancellationToken = default) { _engagements[engagement.Id] = engagement; return Task.CompletedTask; }
    public Task<Engagement?> GetEngagementAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_engagements.TryGetValue(id, out var value) ? value : null);
    public Task<IReadOnlyCollection<Engagement>> GetEngagementsForProfessionalAsync(Guid professionalId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<Engagement>>(_engagements.Values.Where(x => x.ProfessionalId == professionalId).OrderByDescending(x => x.UpdatedAt).ToArray());
    public Task<IReadOnlyCollection<Engagement>> GetEngagementsForCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<Engagement>>(_engagements.Values.Where(x => x.CustomerId == customerId).OrderByDescending(x => x.UpdatedAt).ToArray());
    public Task<IReadOnlyCollection<Engagement>> GetUnacknowledgedEngagementsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<Engagement>>(_engagements.Values.Where(x => x.Status == EngagementStatus.Delivered && x.UpdatedAt <= olderThan).ToArray());
    public Task<ProfessionalNotificationPolicy> GetNotificationPolicyAsync(Guid professionalId, CancellationToken cancellationToken = default) => _notificationPolicies.TryGetValue(professionalId, out var policy) ? Task.FromResult(policy) : Task.FromResult(new ProfessionalNotificationPolicy(professionalId, EngagementChannel.WhatsApp, null, EngagementChannel.Sms, TimeSpan.FromMinutes(10), [EngagementChannel.WhatsApp, EngagementChannel.Sms], []));
    public Task SaveNotificationPolicyAsync(ProfessionalNotificationPolicy policy, CancellationToken cancellationToken = default) { _notificationPolicies[policy.ProfessionalId] = policy; return Task.CompletedTask; }
    public Task SaveDeliveryAttemptAsync(EngagementDeliveryAttempt attempt, CancellationToken cancellationToken = default) { _deliveryAttempts.GetOrAdd(attempt.EngagementId, _ => new ConcurrentQueue<EngagementDeliveryAttempt>()).Enqueue(attempt); return Task.CompletedTask; }
    public Task<IReadOnlyCollection<EngagementDeliveryAttempt>> GetDeliveryAttemptsAsync(Guid engagementId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<EngagementDeliveryAttempt>>(_deliveryAttempts.TryGetValue(engagementId, out var attempts) ? attempts.ToArray() : []);
    private void AddSeedReview(Review review) => _reviews[review.Id] = review;
}

public sealed class FallbackEngagementNotifier : IEngagementNotifier
{
    public Task<EngagementChannel?> DeliverAsync(Professional professional, CustomerIdentity customer, Engagement engagement, ProfessionalNotificationPolicy policy, IReadOnlyCollection<EngagementDeliveryAttempt> previousAttempts, CancellationToken cancellationToken = default)
    {
        var attempted = previousAttempts.Where(x => x.Succeeded).Select(x => x.Channel).ToHashSet();
        EngagementChannel? next = policy.BuildRoute().Where(channel => !attempted.Contains(channel) && policy.HasEndpoint(channel)).Select(channel => (EngagementChannel?)channel).FirstOrDefault();
        return Task.FromResult(next);
    }
    public Task NotifyCustomerAsync(CustomerIdentity customer, Professional professional, Engagement engagement, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
