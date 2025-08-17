using Proton.Sdk.Drive;
using System.Collections.Concurrent;

namespace unofficial_pdrive_cli;

public sealed class ShareMetadataCache
{
    private readonly ConcurrentDictionary<string, ShareMetadata> _cache = new();
    private readonly ProtonDriveClient _client;

    public ShareMetadataCache(ProtonDriveClient client)
    {
        _client = client;
    }

    public async Task<ShareMetadata> GetShareMetadata(string shareId, CancellationToken ct)
    {
        if (!_cache.TryGetValue(shareId, out var shareMetadata))
        {
            var share = await _client.GetShareAsync(new ShareId(shareId), ct);
            shareMetadata = new()
            {
                ShareId = share.ShareId,
                MembershipAddressId = share.MembershipAddressId,
                MembershipEmailAddress = share.MembershipEmailAddress,
            };
            _cache[shareId] = shareMetadata;
        }

        return shareMetadata.Clone();
    }
}
