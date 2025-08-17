namespace unofficial_pdrive_cli;

public interface ITentativeFileStreamFactory
{
    ITentativeFileStream Open(string path);
}
