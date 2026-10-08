using System.Collections.Concurrent;
using Grpc.Core;
using Laraue.Apps.Identity.Internal.Contracts;

namespace Laraue.Apps.LearnLanguage.AppServices.Identity;

/// <summary>
/// Stands in for the real gRPC-backed client when running locally without a live Laraue.Apps.Identity
/// (see "MockExternalServices"). Like the real service, a Telegram id always resolves to the same id.
/// </summary>
public class FakeUserIdentityServiceClient : UserIdentityService.UserIdentityServiceClient
{
    private readonly ConcurrentDictionary<long, string> _userIds = new();

    public override AsyncUnaryCall<CreateUserIfNotExistsResponse> CreateUserIfNotExistsAsync(
        CreateUserIfNotExistsRequest request,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateUserIfNotExistsResponse
        {
            UserId = _userIds.GetOrAdd(request.TelegramId, _ => Guid.NewGuid().ToString()),
        };

        return new AsyncUnaryCall<CreateUserIfNotExistsResponse>(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => global::Grpc.Core.Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
    }
}
