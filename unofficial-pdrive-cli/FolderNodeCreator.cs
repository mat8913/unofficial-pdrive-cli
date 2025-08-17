using Microsoft.Extensions.Logging;
using Proton.Sdk.Drive;
using System.Collections.Immutable;

namespace unofficial_pdrive_cli;
public sealed class FolderNodeCreator
{
    private readonly ILogger<FolderNodeCreator> _logger;
    private readonly ProtonDriveClient _client;
    private readonly NodeLister _lister;
    private readonly ShareMetadataCache _shareMetadataCache;

    public FolderNodeCreator(ILogger<FolderNodeCreator> logger, ProtonDriveClient client, NodeLister lister, ShareMetadataCache shareMetadataCache)
    {
        _logger = logger;
        _client = client;
        _lister = lister;
        _shareMetadataCache = shareMetadataCache;
    }

    public async Task<FolderNode> CreateFolderAsync(INodeIdentity parentNode, string name, CancellationToken ct)
    {
        var shareMetadata = await _shareMetadataCache.GetShareMetadata(parentNode.ShareId.Value, ct);

        _logger.LogInformation("Creating folder {name}", name);

        var newFolderNode = await _client.CreateFolderAsync(shareMetadata, parentNode, name, ct);

        _lister.InvalidateCache(parentNode);

        if (newFolderNode.NodeIdentity.ShareId is null)
        {
            newFolderNode.NodeIdentity.ShareId = new(parentNode.ShareId.Value);
        }

        return newFolderNode;
    }

    public async Task<INode> FindNodeOrCreateFolderAsync(INodeIdentity? startingFrom, ImmutableList<string> targetPath, CancellationToken ct)
    {
        if (startingFrom is null)
        {
            var volumes = await _client.GetVolumesAsync(ct);
            var mainVolume = volumes[0];
            var share = await _client.GetShareAsync(mainVolume.RootShareId, ct);

            startingFrom = new NodeIdentity(share.ShareId, mainVolume.Id, share.RootNodeId);
        }

        INode currentNode = new FolderNode()
        {
            NodeIdentity = new(startingFrom.ShareId, startingFrom.VolumeId, startingFrom.NodeId),
        };

        while (targetPath.Count > 0)
        {
            (_, var child) = await _lister
                .ListNodes(ImmutableList<string>.Empty, currentNode.NodeIdentity, (_, _) => false, ct)
                .FirstOrDefaultAsync(x => x.Node.Name == targetPath[0], ct);

            if (child is null)
            {
                child = await CreateFolderAsync(currentNode.NodeIdentity, targetPath[0], ct);
            }
            else
            {
                _logger.LogInformation("Found existing child {name}", child.Name);
            }

            currentNode = child;
            targetPath = targetPath.RemoveAt(0);
        }

        return currentNode;
    }
}
