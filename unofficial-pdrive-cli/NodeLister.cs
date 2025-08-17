using Proton.Sdk.Drive;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace unofficial_pdrive_cli;

public sealed class NodeLister
{
    private readonly ConcurrentDictionary<NodeKey, ImmutableArray<INode>> _childNodeCache = new();
    private readonly ProtonDriveClient _client;

    public NodeLister(ProtonDriveClient client)
    {
        _client = client;
    }

    public async IAsyncEnumerable<(ImmutableList<string> Path, INode Node)> ListNodes(
        ImmutableList<string> path,
        INodeIdentity nodeIdentity,
        Func<ImmutableList<string>, FolderNode, bool> shouldDescend,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var key = new NodeKey(nodeIdentity.NodeId.Value, nodeIdentity.VolumeId.Value, nodeIdentity.ShareId.Value);
        if (!_childNodeCache.TryGetValue(key, out var children))
        {
            var childrenAsync = _client.GetFolderChildrenAsync(nodeIdentity, ct);

            var builder = ImmutableArray.CreateBuilder<INode>();

            await foreach (var child in childrenAsync)
            {
                if (child.NodeIdentity.ShareId is null)
                {
                    child.NodeIdentity.ShareId = new(nodeIdentity.ShareId.Value);
                }

                builder.Add(child);
            }

            children = builder.ToImmutable();
            _childNodeCache[key] = children;
        }

        foreach (var childOrig in children)
        {
            var child = CloneNode(childOrig);

            var childPath = path.Add(child.Name);
            yield return (childPath, child);
            if (child is FolderNode folder && shouldDescend(childPath, folder))
            {
                await foreach (var subchild in ListNodes(childPath, folder.NodeIdentity, shouldDescend, ct))
                {
                    yield return subchild;
                }
            }
        }
    }

    public async Task<INode?> TryFindNode(INodeIdentity? startingFrom, ImmutableList<string> targetPath, CancellationToken ct)
    {
        if (startingFrom is null)
        {
            var volumes = await _client.GetVolumesAsync(ct);
            var mainVolume = volumes[0];
            var share = await _client.GetShareAsync(mainVolume.RootShareId, ct);

            startingFrom = new NodeIdentity(share.ShareId, mainVolume.Id, share.RootNodeId);
        }

        if (targetPath.Count == 0)
        {
            return new FolderNode()
            {
                NodeIdentity = new(startingFrom.ShareId, startingFrom.VolumeId, startingFrom.NodeId),
            };
        }

        (_, var node) = await
            ListNodes(
                ImmutableList<string>.Empty,
                startingFrom,
                (nodePath, _) => targetPath.StartsWith(nodePath),
                ct)
            .FirstOrDefaultAsync(x => x.Path.SequenceEqual(targetPath), ct);

        return node;
    }

    public void InvalidateCache(INodeIdentity nodeIdentity)
    {
        var key = new NodeKey(nodeIdentity.NodeId.Value, nodeIdentity.VolumeId.Value, nodeIdentity.ShareId.Value);
        _childNodeCache.TryRemove(key, out _);
    }

    [return: NotNullIfNotNull(nameof(node))]
    private static INode? CloneNode(INode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case FolderNode folderNode:
                return folderNode.Clone();
            case FileNode fileNode:
                return fileNode.Clone();
            default:
                throw new ArgumentException($"Unhandled node type: {node.GetType()}");
        }
    }

    private readonly record struct NodeKey(string NodeId, string VolumeId, string ShareId);
}
