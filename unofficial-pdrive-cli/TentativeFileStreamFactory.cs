namespace unofficial_pdrive_cli;

public sealed class TentativeFileStreamFactory : ITentativeFileStreamFactory
{
    private TentativeFileStreamFactory()
    {
    }

    public static ITentativeFileStreamFactory Instance { get; } = new TentativeFileStreamFactory();

    public ITentativeFileStream Open(string path)
    {
        return new Impl(path);
    }

    private sealed class Impl : ITentativeFileStream
    {
        private readonly string _tmppath;
        private readonly string _finalpath;
        private bool _disposed = false;

        public Impl(string path)
        {
            var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("GetDirectoryName failed", nameof(path));
            _tmppath = Path.Combine(directory, Path.GetRandomFileName());
            Stream = new FileStream(_tmppath, FileMode.CreateNew);
            _finalpath = path;
        }

        public Stream Stream { get; private init; }

        public void Dispose()
        {
            if (!_disposed)
            {
                Stream.Dispose();
                File.Delete(_tmppath);
                _disposed = true;
            }
        }

        public void Save(bool overwrite = false)
        {
            if (!_disposed)
            {
                Stream.Dispose();
                File.Move(_tmppath, _finalpath, overwrite);
                _disposed = true;
            }
        }
    }
}
