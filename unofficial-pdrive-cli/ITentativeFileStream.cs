namespace unofficial_pdrive_cli;

public interface ITentativeFileStream : IDisposable
{
    Stream Stream { get; }

    void Save(bool overwrite = false);
}
