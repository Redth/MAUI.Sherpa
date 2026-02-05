using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Publisher;

namespace MauiSherpa.Core.Handlers.Publisher;

/// <summary>
/// Handler for listing repositories from a secrets publisher.
/// Uses caching with offline availability for fast UI response.
/// </summary>
public partial class ListPublisherRepositoriesHandler 
    : IRequestHandler<ListPublisherRepositoriesRequest, IReadOnlyList<PublisherRepository>>
{
    private readonly ISecretsPublisherService _publisherService;
    private readonly ISecretsPublisherFactory _publisherFactory;
    private readonly ILoggingService _logger;

    public ListPublisherRepositoriesHandler(
        ISecretsPublisherService publisherService,
        ISecretsPublisherFactory publisherFactory,
        ILoggingService logger)
    {
        _publisherService = publisherService;
        _publisherFactory = publisherFactory;
        _logger = logger;
    }

    [Cache(AbsoluteExpirationSeconds = 300)] // 5 minute cache
    [OfflineAvailable] // Show cached data even when offline
    public async Task<IReadOnlyList<PublisherRepository>> Handle(
        ListPublisherRepositoriesRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        var config = await _publisherService.GetPublisherAsync(request.PublisherId);
        if (config == null)
        {
            _logger.LogWarning($"Publisher not found: {request.PublisherId}");
            return Array.Empty<PublisherRepository>();
        }

        var publisher = _publisherFactory.CreatePublisher(config);
        var repos = await publisher.ListRepositoriesAsync(cancellationToken: ct);
        
        _logger.LogDebug($"Loaded {repos.Count} repositories from {config.Name}");
        return repos;
    }
}
