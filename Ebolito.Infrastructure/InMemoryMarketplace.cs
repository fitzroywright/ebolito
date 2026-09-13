using System.Collections.Concurrent;
using Ebolito.Application;
using Ebolito.Domain;

namespace Ebolito.Infrastructure;

public sealed class InMemoryMarketplaceStore : IMarketplaceStore
{
    private readonly IReadOnlyCollection<Skill> _skills;
    private readonly IReadOnlyCollection<Professional> _professionals;
    private readonly IReadOnlyCollection<PortfolioProject> _projects;
    private readonly IReadOnlyCollection<Review> _reviews;
    private readonly ConcurrentDictionary<Guid, CustomerIdentity> _customers = new();
    private readonly ConcurrentDictionary<Guid, Engagement> _engagements = new();

    public InMemoryMarketplaceStore()
    {
        var plumbing = new Skill(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Plumbing", ["plumber", "pipe repair", "leak"]);
        var carpentry = new Skill(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Carpentry", ["carpenter", "cabinetry", "woodwork"]);
        var electrical = new Skill(Guid.Parse("33333333-3333-3333-3333-333333333333"), "Electrical", ["electrician", "wiring", "electrical installation"]);
        _skills = [plumbing, carpentry, electrical];

        var beverlyId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var marcusId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        _professionals =
        [
            new Professional(beverlyId, "beverly-hyman", "Beverly Hyman", "Hyman Plumbing Services", "Reliable plumbing for homes and businesses", "Experienced plumber serving Kingston and St. Andrew. Repairs, installations and emergency work.", "+18765550101", "+18765550101", [plumbing.Id], [new ServiceArea("Kingston"), new ServiceArea("St. Andrew")], true),
            new Professional(marcusId, "marcus-brown", "Marcus Brown", "Brown Custom Woodwork", "Custom kitchens, cabinetry and furniture", "Custom carpentry focused on durable, practical work and clean finishes.", "+18765550102", "+18765550102", [carpentry.Id], [new ServiceArea("St. Catherine", "Spanish Town"), new ServiceArea("Kingston")], true)
        ];

        _projects =
        [
            new PortfolioProject(Guid.NewGuid(), beverlyId, "Kitchen Pipe Repair", "Replaced damaged supply lines and restored the kitchen sink installation.", "Kingston", DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-2)), [plumbing.Id], [new PortfolioPhoto(Guid.NewGuid(), "/images/demo/plumbing-1.jpg", "Completed kitchen plumbing")], true),
            new PortfolioProject(Guid.NewGuid(), marcusId, "Modern Kitchen Cabinets", "Built and installed a full custom cabinet set with island storage.", "Spanish Town", DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-4)), [carpentry.Id], [new PortfolioPhoto(Guid.NewGuid(), "/images/demo/carpentry-1.jpg", "Custom kitchen cabinetry")], true)
        ];

        _reviews =
        [
            new Review(Guid.NewGuid(), beverlyId, null, "C. Wallace", 5, "Professional, on time and very knowledgeable.", DateTimeOffset.UtcNow.AddDays(-35), false),
            new Review(Guid.NewGuid(), marcusId, null, "A. Grant", 5, "Excellent workmanship and communication.", DateTimeOffset.UtcNow.AddDays(-18), false)
        ];

        var demoCustomer = new CustomerIdentity(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "Demo Customer", "+18765550999", "customer@example.com");
        _customers[demoCustomer.Id] = demoCustomer;
    }

    public Task<IReadOnlyCollection<Skill>> GetSkillsAsync(CancellationToken cancellationToken = default) => Task.FromResult(_skills);
    public Task<IReadOnlyCollection<Professional>> GetProfessionalsAsync(CancellationToken cancellationToken = default) => Task.FromResult(_professionals);
    public Task<Professional?> GetProfessionalAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_professionals.FirstOrDefault(x => x.Id == id));
    public Task<Professional?> GetProfessionalBySlugAsync(string slug, CancellationToken cancellationToken = default) => Task.FromResult(_professionals.FirstOrDefault(x => x.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase)));
    public Task<IReadOnlyCollection<PortfolioProject>> GetProjectsAsync(Guid professionalId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<PortfolioProject>>(_projects.Where(x => x.ProfessionalId == professionalId).OrderByDescending(x => x.IsFeatured).ThenByDescending(x => x.CompletedOn).ToArray());
    public Task<IReadOnlyCollection<Review>> GetReviewsAsync(Guid professionalId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<Review>>(_reviews.Where(x => x.ProfessionalId == professionalId).OrderByDescending(x => x.CreatedAt).ToArray());
    public Task<CustomerIdentity?> GetCustomerAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_customers.TryGetValue(id, out var value) ? value : null);
    public Task<CustomerIdentity?> GetCustomerByMobileAsync(string mobileNumber, CancellationToken cancellationToken = default) => Task.FromResult(_customers.Values.FirstOrDefault(x => x.VerifiedMobileNumber.Equals(mobileNumber, StringComparison.OrdinalIgnoreCase)));
    public Task SaveCustomerAsync(CustomerIdentity customer, CancellationToken cancellationToken = default) { _customers[customer.Id] = customer; return Task.CompletedTask; }
    public Task SaveEngagementAsync(Engagement engagement, CancellationToken cancellationToken = default) { _engagements[engagement.Id] = engagement; return Task.CompletedTask; }
    public Task<Engagement?> GetEngagementAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_engagements.TryGetValue(id, out var value) ? value : null);
}

public sealed class FallbackEngagementNotifier : IEngagementNotifier
{
    public Task<EngagementChannel> DeliverAsync(Professional professional, CustomerIdentity customer, Engagement engagement, CancellationToken cancellationToken = default)
    {
        var channel = engagement.RequestedChannel switch
        {
            EngagementChannel.WhatsApp when !string.IsNullOrWhiteSpace(professional.WhatsAppNumber) => EngagementChannel.WhatsApp,
            EngagementChannel.WhatsApp when !string.IsNullOrWhiteSpace(professional.PhoneNumber) => EngagementChannel.Sms,
            EngagementChannel.Sms when !string.IsNullOrWhiteSpace(professional.PhoneNumber) => EngagementChannel.Sms,
            _ => EngagementChannel.Web
        };
        return Task.FromResult(channel);
    }

    public Task NotifyCustomerAsync(CustomerIdentity customer, Professional professional, Engagement engagement, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
