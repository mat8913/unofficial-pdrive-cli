using Microsoft.Extensions.Logging;
using Proton.Sdk.Drive;
using System.Collections.Immutable;

namespace unofficial_pdrive_cli;

public sealed class NodeUploader
{
    private readonly ILogger<NodeUploader> _logger;
    private readonly ProtonDriveClient _client;
    private readonly LocalHashCache _localHashCache;
    private readonly NodeDownloader _nodeDownloader;
    private readonly ShareMetadataCache _shareMetadataCache;
    private readonly FolderNodeCreator _folderCreator;
    private readonly NodeLister _lister;

    public NodeUploader(
        ILogger<NodeUploader> logger,
        ProtonDriveClient client,
        LocalHashCache localHashCache,
        NodeDownloader nodeDownloader,
        ShareMetadataCache shareMetadataCache,
        FolderNodeCreator folderCreator,
        NodeLister lister)
    {
        _logger = logger;
        _client = client;
        _localHashCache = localHashCache;
        _nodeDownloader = nodeDownloader;
        _shareMetadataCache = shareMetadataCache;
        _folderCreator = folderCreator;
        _lister = lister;
    }

    public async Task UploadNode(
        INodeIdentity? startingFrom,
        string src,
        ImmutableList<string> target,
        TargetType targetType,
        bool overwrite,
        CancellationToken ct,
        Action<double>? onProgress)
    {
        if (startingFrom is null)
        {
            var volumes = await _client.GetVolumesAsync(ct);
            var mainVolume = volumes[0];
            var share = await _client.GetShareAsync(mainVolume.RootShareId, ct);

            startingFrom = new NodeIdentity(share.ShareId, mainVolume.Id, share.RootNodeId);
        }

        NodeIdentity parentNode;
        string destName;
        switch (targetType)
        {
            case TargetType.Folder:
                {
                    var folder = await _folderCreator.FindNodeOrCreateFolderAsync(startingFrom, target, ct);
                    parentNode = folder.NodeIdentity;
                    destName = Path.GetFileName(src);
                    break;
                }
            case TargetType.File:
                {
                    var folder = await _folderCreator.FindNodeOrCreateFolderAsync(startingFrom, target.RemoveAt(target.Count - 1), ct);
                    parentNode = folder.NodeIdentity;
                    destName = target[^1];
                    break;
                }
            case TargetType.Unspecified:
                {
                    var node = await _lister.TryFindNode(startingFrom, target, ct);
                    if (node is null || node is FileNode)
                    {
                        // Treat as TargetType.File
                        var folder = await _folderCreator.FindNodeOrCreateFolderAsync(startingFrom, target.RemoveAt(target.Count - 1), ct);
                        parentNode = folder.NodeIdentity;
                        destName = target[^1];
                    }
                    else
                    {
                        // Treat as TargetType.Folder
                        var folder = await _folderCreator.FindNodeOrCreateFolderAsync(startingFrom, target, ct);
                        parentNode = folder.NodeIdentity;
                        destName = Path.GetFileName(src);
                    }
                    break;
                }
            default:
                throw new ArgumentException($"Unknown TargetType: {targetType}", nameof(targetType));
        }

        await UploadNode(src, parentNode, destName, overwrite, ct, onProgress);
    }

    public async Task UploadNode(
        string src,
        NodeIdentity parentNode,
        string destName,
        bool overwrite,
        CancellationToken ct,
        Action<double>? onProgress)
    {
        onProgress ??= _ => { };

        using var fileStream = new FileStream(src, FileMode.Open, FileAccess.Read);
        DateTimeOffset mtime = File.GetLastWriteTimeUtc(fileStream.SafeFileHandle);

        var localHash = _localHashCache.GetOrUpdate(fileStream);
        fileStream.Position = 0;

        var existingNode = await _lister.TryFindNode(parentNode, ImmutableList.Create(destName), ct);
        if (existingNode is not null)
        {
            var existingHash = await _nodeDownloader.GetNodeHash((FileNode)existingNode, null, ct);
            if (existingHash == localHash)
            {
                _logger.LogInformation("Skipping upload because hashes match: {hash} {dest}", existingHash, destName);
                return;
            }
            if (!overwrite)
            {
                _logger.LogWarning("Skipping due to conflict: {dest}", destName);
                _logger.LogInformation("{localHash} != {remoteHash}", localHash, existingHash);
                return;
            }
            _logger.LogWarning("Overwriting: {dest}", destName);
        }

        using var uploader = await _client.WaitForFileUploaderAsync(fileStream.Length, 0, ct);

        var shareMetadata = await _shareMetadataCache.GetShareMetadata(parentNode.ShareId.Value, ct);

        var response = await UploaderUploadFileAsync(
            uploader,
            shareMetadata,
            parentNode,
            destName,
            "application/octet-stream",
            fileStream,
            Enumerable.Empty<FileSample>(),
            mtime,
            (x, y) => onProgress(((double)x) / (double)y),
            ct,
            overwrite);

        _lister.InvalidateCache(parentNode);

        var node = await _client.GetNodeAsync(parentNode.ShareId, response.NodeIdentity.NodeId, ct);

        if (node.State == NodeState.Draft)
        {
            throw new InvalidOperationException($"Node ended up in Draft state when uploading {src}");
        }

        node.NodeIdentity.ShareId = new(parentNode.ShareId.Value);
        var remoteHash = await _nodeDownloader.GetNodeHash((FileNode)node, null, ct);

        if (localHash != remoteHash)
        {
            throw new InvalidOperationException($"Hash mismatch when uploading {src}. L:{localHash} != R:{remoteHash}");
        }
    }

    private static async Task<FileNode> UploaderUploadFileAsync(
        IFileUploader uploader,
        ShareMetadata shareMetadata,
        NodeIdentity parentFolderIdentity,
        string name,
        string mediaType,
        Stream contentInputStream,
        IEnumerable<FileSample> samples,
        DateTimeOffset? lastModificationTime,
        Action<long, long> onProgress,
        CancellationToken cancellationToken,
        bool overwrite)
    {
        if (overwrite)
        {
            return await uploader.UploadNewFileOrRevisionAsync(
                shareMetadata,
                parentFolderIdentity,
                name,
                mediaType,
                contentInputStream,
                samples,
                lastModificationTime,
                onProgress,
                cancellationToken);
        }
        else
        {
            return (await uploader.UploadNewFileAsync(
                shareMetadata,
                parentFolderIdentity,
                name,
                mediaType,
                contentInputStream,
                samples,
                lastModificationTime,
                onProgress,
                cancellationToken)).File;
        }
    }
}

public enum TargetType
{
    Unspecified,
    File,
    Folder,
}
