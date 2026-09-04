namespace SecureVault.Core.Exceptions;

public class VaultException : Exception
{
    public VaultException(string message) : base(message) { }
    public VaultException(string message, Exception innerException) : base(message, innerException) { }
}

public class VaultAlreadyOpenException : VaultException
{
    public VaultAlreadyOpenException(string message) : base(message) { }
}

public class VaultAlreadyExistsException : VaultException
{
    public VaultAlreadyExistsException(string message) : base(message) { }
}

public class InvalidPasswordException : VaultException
{
    public InvalidPasswordException(string message = "The supplied password is invalid.") : base(message) { }
    public InvalidPasswordException(string message, Exception innerException) : base(message, innerException) { }
}

public class InvalidRecoveryKeyException : VaultException
{
    public InvalidRecoveryKeyException(string message = "The supplied recovery key is invalid.") : base(message) { }
    public InvalidRecoveryKeyException(string message, Exception innerException) : base(message, innerException) { }
}

public class CorruptedChunkException : VaultException
{
    public int ChunkIndex { get; }
    public CorruptedChunkException(int chunkIndex, string message) : base(message)
    {
        ChunkIndex = chunkIndex;
    }
    public CorruptedChunkException(int chunkIndex, string message, Exception innerException) : base(message, innerException)
    {
        ChunkIndex = chunkIndex;
    }
}

public class CorruptedIndexException : VaultException
{
    public CorruptedIndexException(string message) : base(message) { }
    public CorruptedIndexException(string message, Exception innerException) : base(message, innerException) { }
}

public class CorruptedVaultException : VaultException
{
    public CorruptedVaultException(string message) : base(message) { }
    public CorruptedVaultException(string message, Exception innerException) : base(message, innerException) { }
}

public class VaultLockedException : VaultException
{
    public VaultLockedException(string message = "The vault is currently locked.") : base(message) { }
}

public class UncorrectableCorruptionException : VaultException
{
    public UncorrectableCorruptionException(string message) : base(message) { }
    public UncorrectableCorruptionException(string message, Exception innerException) : base(message, innerException) { }
}

public class VaultPartMissingException : VaultException
{
    public int PartIndex { get; }
    public string ExpectedFileName { get; }

    public VaultPartMissingException(int partIndex, string expectedFileName, string message) : base(message)
    {
        PartIndex = partIndex;
        ExpectedFileName = expectedFileName;
    }
}

public class IncompleteBackupException : VaultException
{
    public IReadOnlyList<string> MissingOrCorruptParts { get; }

    public IncompleteBackupException(string message, IReadOnlyList<string> failedParts) : base(message)
    {
        MissingOrCorruptParts = failedParts;
    }
}

