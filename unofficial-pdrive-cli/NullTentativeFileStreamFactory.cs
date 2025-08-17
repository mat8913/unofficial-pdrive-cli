namespace unofficial_pdrive_cli;

public sealed class NullTentativeFileStreamFactory : ITentativeFileStreamFactory
{
    private NullTentativeFileStreamFactory()
    {
    }

    public static ITentativeFileStreamFactory Instance { get; } = new NullTentativeFileStreamFactory();

    public ITentativeFileStream Open(string path)
    {
        return new Impl();
    }

    private sealed class Impl : ITentativeFileStream
    {
        public Stream Stream => System.IO.Stream.Null;

        public void Dispose()
        {
        }

        public void Save(bool overwrite = false)
        {
        }
    }
}
