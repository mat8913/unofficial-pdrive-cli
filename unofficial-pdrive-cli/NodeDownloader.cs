using Microsoft.Extensions.Logging;
using Proton.Sdk.Drive;
using System.Security.Cryptography;

namespace unofficial_pdrive_cli;

public sealed class NodeDownloader
{
    private readonly ILogger<NodeDownloader> _logger;
    private readonly ProtonDriveClient _client;
    private readonly LocalHashCache _localHashCache;
    private readonly RemoteHashCache _remoteHashCache;
    private readonly ITentativeFileStreamFactory _streamFactory;

    public NodeDownloader(
        ILogger<NodeDownloader> logger,
        ProtonDriveClient client,
        LocalHashCache localHashCache,
        RemoteHashCache remoteHashCache,
        ITentativeFileStreamFactory streamFactory)
    {
        _logger = logger;
        _client = client;
        _localHashCache = localHashCache;
        _remoteHashCache = remoteHashCache;
        _streamFactory = streamFactory;
    }

    public async Task DownloadNode(
        FileNode fileNode,
        IRevisionForTransfer? revision,
        string dest,
        bool overwrite,
        CancellationToken ct,
        Action<double>? onProgress)
    {
        revision ??= fileNode.ActiveRevision;
        onProgress ??= _ => { };
        dest = Path.GetFullPath(dest);

        if (revision is null)
        {
            _logger.LogWarning("No active revision while downloading {dest}", dest);
            var revisions = await _client.GetFileRevisionsAsync(fileNode.NodeIdentity, ct);
            revision = revisions[0];
        }

        var hasCachedLocalHash = _localHashCache.TryGetOrUpdate(dest, out var cachedLocalHash);
        var hashCachedRemoteHash = _remoteHashCache.TryGet(fileNode.NodeIdentity, revision, out var cachedRemoteHash);
        if (hasCachedLocalHash && hashCachedRemoteHash)
        {
            if (cachedLocalHash == cachedRemoteHash)
            {
                _logger.LogInformation("Skipping download because hashes match: {hash} {dest}", cachedLocalHash, dest);
                return;
            }
            if (!overwrite)
            {
                _logger.LogWarning("Skipping due to conflict: {dest}", dest);
                _logger.LogInformation("{localHash} != {remoteHash}", cachedLocalHash, cachedRemoteHash);
                return;
            }
        }

        using var downloader = await _client.WaitForFileDownloaderAsync(ct);

        string hash;
        using (var stream = _streamFactory.Open(dest))
        {
            using var hashAlgo = SriHasher.CreateHashAlgo();
            using (var hashStream = new CryptoStreamPosition(stream.Stream, hashAlgo, CryptoStreamMode.Write, true))
            {
                onProgress(0);
                var verification = await downloader.DownloadAsync(
                    fileNode.NodeIdentity,
                    revision,
                    hashStream,
                    (x, y) => onProgress(((double)x) / (double)y),
                    ct);
                onProgress(1);
                if (verification != VerificationStatus.Ok)
                {
                    throw new Exception($"Verification failed: {verification}");
                }
            }
            hash = SriHasher.GetHash(hashAlgo);
            _remoteHashCache.Add(fileNode.NodeIdentity, revision, hash);
            if (hasCachedLocalHash)
            {
                if (hash == cachedLocalHash)
                {
                    _logger.LogInformation("Skipping write because hashes match: {hash} {dest}", hash, dest);
                    return;
                }
                else if (overwrite)
                {
                    _logger.LogWarning("Overwriting: {dest}", dest);
                }
                else
                {
                    _logger.LogWarning("Skipping due to conflict: {dest}", dest);
                    _logger.LogInformation("{localHash} != {remoteHash}", cachedLocalHash, hash);
                    return;
                }
            }
            stream.Save(overwrite);
        }

        _localHashCache.Add(dest, File.GetLastWriteTimeUtc(dest), hash);
    }

    public async Task<string> GetNodeHash(
        FileNode fileNode,
        IRevisionForTransfer? revision,
        CancellationToken ct)
    {
        revision ??= fileNode.ActiveRevision;

        if (_remoteHashCache.TryGet(fileNode.NodeIdentity, revision, out var cachedRemoteHash))
        {
            return cachedRemoteHash;
        }

        using var downloader = await _client.WaitForFileDownloaderAsync(ct);

        using var hashAlgo = SriHasher.CreateHashAlgo();
        using (var hashStream = new CryptoStreamPosition(Stream.Null, hashAlgo, CryptoStreamMode.Write, true))
        {
            var verification = await downloader.DownloadAsync(
                fileNode.NodeIdentity,
                revision,
                hashStream,
                (_, _) => { },
                ct);
            if (verification != VerificationStatus.Ok)
            {
                throw new Exception($"Verification failed: {verification}");
            }
        }
        var hash = SriHasher.GetHash(hashAlgo);
        _remoteHashCache.Add(fileNode.NodeIdentity, revision, hash);
        return hash;
    }
}
