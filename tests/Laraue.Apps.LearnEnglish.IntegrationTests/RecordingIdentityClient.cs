using Grpc.Core;
using Laraue.Apps.Identity.Internal.Contracts;
using Laraue.Apps.LearnLanguage.AppServices.Identity;

namespace Laraue.Apps.LearnEnglish.IntegrationTests;

/// <summary>
/// An Identity client that keeps the requests it gets. Like Identity, it returns the sent source lower-cased.
/// </summary>
public class RecordingIdentityClient : FakeUserIdentityServiceClient
{
    private readonly Dictionary<long, Guid> _ids = new();

    public bool Fail { get; set; }

    public List<CreateUserIfNotExistsRequest> Requests { get; } = [];

    public Guid GlobalIdOf(long telegramId) => _ids[telegramId];

    public override AsyncUnaryCall<CreateUserIfNotExistsResponse> CreateUserIfNotExistsAsync(
        CreateUserIfNotExistsRequest request,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        if (Fail)
            throw new RpcException(new Status(StatusCode.Unavailable, "Identity is down"));

        Requests.Add(request);
        if (!_ids.ContainsKey(request.TelegramId))
            _ids[request.TelegramId] = Guid.NewGuid();

        var response = new CreateUserIfNotExistsResponse { UserId = _ids[request.TelegramId].ToString() };
        if (request.HasSource)
            response.Source = request.Source.ToLowerInvariant();

        return new AsyncUnaryCall<CreateUserIfNotExistsResponse>(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
    }
}
