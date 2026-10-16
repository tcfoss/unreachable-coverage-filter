using System.Security.Cryptography;

namespace TcfOss.UnreachableCoverageFilter;

public sealed class FileHasher
{
    private static readonly Guid s_sha256Guid = new("8829d00f-11b8-4213-878b-770e8597ac16");
    private static readonly Guid s_sha1Guid = new("ff1816ec-aa5e-4d10-87f7-6f4963833460");

    public Func<byte[], byte[]> HashData { get; }

    private FileHasher(Func<byte[], byte[]> hashData)
    {
        HashData = hashData;
    }

    public static FileHasher? GetHasher(Guid value)
    {
        if (value == s_sha256Guid)
        {
            return Sha256;
        }
        if (value == s_sha1Guid)
        {
            return Sha1;
        }
        return null;
    }

    public static FileHasher Sha256 { get; } = new(SHA256.HashData);
    public static FileHasher Sha1 { get; } = new(SHA1.HashData);
}

