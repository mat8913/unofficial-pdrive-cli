using Microsoft.Extensions.Logging;
using Proton.Sdk;
using Proton.Sdk.Drive;
using System.Collections.Immutable;
using System.Text;

namespace unofficial_pdrive_cli;

public sealed class Program
{
    private const string APP_NAME = "macos-drive@1.0.0-alpha.1+rclone";
    private readonly ILoggerFactory _loggerFactory;

    public Program(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public static Task<int> Main(string[] argv)
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var program = new Program(loggerFactory);
        return program.Run(argv);
    }

    public async Task<int> Run(string[] argv)
    {
        var ct = CancellationToken.None;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
        var dataDir = Path.Join(appData, "unofficial-pdrive-cli");
        var dbFile = Path.Join(dataDir, "data.db");
        Directory.CreateDirectory(dataDir);
        PersistenceManager persistenceManager = new(dbFile);
        SessionStorage sessionStorage = new(persistenceManager);

        var enableSdkLog = false;
        var overwrite = false;
        var recursive = false;

        foreach (var flag in argv.Where(x => x.StartsWith("--")))
        {
            switch (flag)
            {
                case "--enable-sdk-log":
                    enableSdkLog = true;
                    break;
                case "--overwrite":
                    overwrite = true;
                    break;
                case "--recursive":
                    recursive = true;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown flag: {flag}");
                    return -1;
            }
        }

        var args = argv.Where(x => !x.StartsWith("--")).ToArray();

        if (args.Length == 0)
        {
            return InvalidUsage();
        }

        if (args[0] == "login")
        {
            if (args.Length != 1)
            {
                return InvalidUsage();
            }
            await Login(persistenceManager, sessionStorage, enableSdkLog, ct);
            return 0;
        }

        var streamFactory = TentativeFileStreamFactory.Instance;
        var session = ResumeSession(persistenceManager, sessionStorage, enableSdkLog);
        var client = new ProtonDriveClient(session);
        var shareMetadataCache = new ShareMetadataCache(client);
        var nodeLister = new NodeLister(client);
        var localHashCache = new LocalHashCache(persistenceManager);
        var remoteHashCache = new RemoteHashCache(persistenceManager);
        var downloader = new NodeDownloader(
            _loggerFactory.CreateLogger<NodeDownloader>(),
            client,
            localHashCache,
            remoteHashCache,
            streamFactory);
        var folderNodeCreator = new FolderNodeCreator(
            _loggerFactory.CreateLogger<FolderNodeCreator>(),
            client,
            nodeLister,
            shareMetadataCache);
        var uploader = new NodeUploader(
            _loggerFactory.CreateLogger<NodeUploader>(),
            client,
            localHashCache,
            downloader,
            shareMetadataCache,
            folderNodeCreator,
            nodeLister);

        if (args[0] == "get")
        {
            if (args.Length != 3)
            {
                return InvalidUsage();
            }

            var src = args[1];
            var srcSplit = src.Split('/').Where(x => x != string.Empty).ToImmutableList();
            var dest = args[2];
            dest = Path.GetFullPath(dest);

            var node = await nodeLister.TryFindNode(null, srcSplit, ct);
            if (node is null)
            {
                Console.Error.WriteLine($"{src} not found");
                return -1;
            }
            if (node is FileNode fileNode)
            {
                if (Directory.Exists(dest))
                {
                    dest = Path.Combine(dest, fileNode.Name);
                }
                await downloader.DownloadNode(fileNode, null, dest, overwrite, ct, x =>
                    Console.Error.WriteLine($"{src} -> {dest}: {x:P2}")
                );
                return 0;
            }
            if (!recursive)
            {
                Console.Error.WriteLine($"{src} is not a file. Did you want --recursive?");
                return -1;
            }
            if (node is not FolderNode folderNode)
            {
                throw new InvalidOperationException($"node is of unexpected type: {node.GetType()}");
            }
            if (File.Exists(dest))
            {
                Console.Error.WriteLine($"{dest} is a file");
            }
            var children = nodeLister.ListNodes(ImmutableList<string>.Empty, folderNode.NodeIdentity, (_, _) => true, ct);
            await foreach (var child in children)
            {
                if (child.Node is not FileNode childFileNode)
                {
                    continue;
                }
                var fullDestDir = Path.Combine(child.Path.Take(child.Path.Count - 1).Prepend(dest).ToArray());
                var fullDest = Path.Combine(child.Path.Prepend(dest).ToArray());
                var fullSrc = src + (src.EndsWith('/') ? "" : "/") + string.Join('/', child.Path);
                Directory.CreateDirectory(fullDestDir);
                await downloader.DownloadNode(childFileNode, null, fullDest, overwrite, ct, x =>
                    Console.Error.WriteLine($"{fullSrc} -> {fullDest}: {x:P2}")
                );
            }
            return 0;
        }

        if (args[0] == "put")
        {
            if (args.Length != 3)
            {
                return InvalidUsage();
            }

            var src = args[1];
            src = Path.GetFullPath(src);
            var dest = args[2];
            var destSplit = dest.Split('/').Where(x => x != string.Empty).ToImmutableList();

            if (File.Exists(src))
            {
                var targetType = dest.EndsWith('/') ? TargetType.Folder : TargetType.Unspecified;
                await uploader.UploadNode(null, src, destSplit, targetType, overwrite, ct, x =>
                    Console.Error.WriteLine($"{src} -> {dest}: {x:P2}"));
                return 0;
            }

            if (Directory.Exists(src))
            {
                if (!recursive)
                {
                    Console.Error.WriteLine($"{src} is not a file. Did you want --recursive?");
                    return -1;
                }

                var children = Directory.EnumerateFiles(src, "*", new EnumerationOptions()
                {
                    RecurseSubdirectories = true,
                    ReturnSpecialDirectories = false,
                    MatchType = MatchType.Simple,
                });

                foreach (var fullSrc in children)
                {
                    var relPath = Path.GetRelativePath(src, fullSrc);
                    var fullDestSplit = destSplit.AddRange(relPath.Split(Path.DirectorySeparatorChar));
                    var fullDest = string.Join('/', fullDestSplit);

                    Console.WriteLine($"{fullSrc} -> {fullDest}");
                    await uploader.UploadNode(null, fullSrc, fullDestSplit, TargetType.File, overwrite, ct, x =>
                        Console.Error.WriteLine($"{fullSrc} -> {fullDest}: {x:P2}"));
                }

                return 0;
            }

            Console.Error.WriteLine($"{src} does not exist.");
            return -1;
        }

        return InvalidUsage();
    }

    private static int InvalidUsage()
    {
        Console.Error.WriteLine("Invalid usage.");
        Console.Error.WriteLine();
        Console.Error.WriteLine($"Usage: {Environment.GetCommandLineArgs()[0]} [<flags>] <command> [<args>]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Commands:");
        Console.Error.WriteLine();
        Console.Error.WriteLine("    login                          -  logs in to Proton Drive");
        Console.Error.WriteLine("    get <remote src> <local dest>  -  downloads from Proton Drive");
        Console.Error.WriteLine("    put <local src> <remote dest>  -  uploads to Proton Drive");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Flags:");
        Console.Error.WriteLine();
        Console.Error.WriteLine("    --enable-sdk-log  -  enables log output from the Proton SDK");
        Console.Error.WriteLine("    --overwrite       -  allow local and remote files to be overwritten");
        Console.Error.WriteLine("    --recursive       -  recurse into directories");
        return -1;
    }

    private async Task Login(
        PersistenceManager persistenceManager,
        SessionStorage sessionStorage,
        bool enableSdkLog,
        CancellationToken ct)
    {
        Console.Error.Write("Username: ");
        var username = Console.ReadLine();
        Console.Error.Write("Password: ");
        var password = Console.ReadLine();

        var secretsCache = new SqlSecretsCache(persistenceManager);

        var options = new ProtonClientOptions
        {
            AppVersion = APP_NAME,
            SecretsCache = secretsCache,
        };
        if (enableSdkLog)
        {
            options.LoggerFactory = _loggerFactory;
        }

        var sessionBeginRequest = new SessionBeginRequest
        {
            Username = username,
            Password = password,
            Options = options,
        };

        var session = await ProtonApiSession.BeginAsync(sessionBeginRequest, ct);

        if (session.IsWaitingForSecondFactorCode)
        {
            Console.Error.Write("OTP Code: ");
            var otpCode = Console.ReadLine();

            await session.ApplySecondFactorCodeAsync(otpCode!, ct);

            await session.ApplyDataPasswordAsync(Encoding.UTF8.GetBytes(password!), ct);
        }

        var tokens = await session.TokenCredential.GetAccessTokenAsync(ct);

        var storedSession = new StoredSession
        (
            SessionId: session.SessionId.Value,
            Username: session.Username,
            UserId: session.UserId.Value,
            AccessToken: tokens.AccessToken,
            RefreshToken: tokens.RefreshToken,
            Scopes: session.Scopes.ToArray(),
            IsWaitingForSecondFactorCode: session.IsWaitingForSecondFactorCode,
            PasswordMode: (int)session.PasswordMode
        );

        sessionStorage.StoreSession(storedSession);
    }

    private ProtonApiSession ResumeSession(
        PersistenceManager persistenceManager,
        SessionStorage sessionStorage,
        bool enableSdkLog)
    {
        var hasStoredSession = sessionStorage.TryLoadSession(out var savedSession);

        var secretsCache = new SqlSecretsCache(persistenceManager);

        var options = new ProtonClientOptions
        {
            AppVersion = APP_NAME,
            SecretsCache = secretsCache,
        };
        if (enableSdkLog)
        {
            options.LoggerFactory = _loggerFactory;
        }

        var sessionResumeRequest = new SessionResumeRequest
        {
            SessionId = new() { Value = savedSession.SessionId },
            Username = savedSession.Username,
            UserId = new() { Value = savedSession.UserId },
            AccessToken = savedSession.AccessToken,
            RefreshToken = savedSession.RefreshToken,
            IsWaitingForSecondFactorCode = savedSession.IsWaitingForSecondFactorCode,
            PasswordMode = (PasswordMode)savedSession.PasswordMode,
            Options = options,
        };
        sessionResumeRequest.Scopes.AddRange(savedSession.Scopes);

        var session = ProtonApiSession.Resume(sessionResumeRequest);

        session.TokenCredential.TokensRefreshed += (accessToken, refreshToken) =>
        {
            sessionStorage.UpdateTokens(accessToken, refreshToken);
        };

        return session;
    }
}
