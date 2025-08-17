using System.Security.Cryptography;

namespace unofficial_pdrive_cli;

// Workaround hack for CryptoStream not supporting get Position
public sealed class CryptoStreamPosition : CryptoStream
{
    private readonly Stream _stream;

    public CryptoStreamPosition(Stream stream, ICryptoTransform transform, CryptoStreamMode mode, bool leaveOpen)
        : base(stream, transform, mode, leaveOpen)
    {
        _stream = stream;
    }

    public override long Position
    {
        get => _stream.Position;
        set => base.Position = value;
    }
}
