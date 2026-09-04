using SecureVault.Core.Integrity;

namespace SecureVault.Core.Tests;

public class RepairLoggerTests
{
    [Fact]
    public void AssertAndLogRepair_LogsEventAndEnforcesReVerification()
    {
        var logger = new RepairLogger();
        Guid testGuid = Guid.NewGuid();

        RepairEvent? capturedEvent = null;
        logger.RepairLogged += (s, e) => capturedEvent = e;

        // Valid repair with passed verification
        bool committed = logger.AssertAndLogRepair(
            testGuid,
            "document.pdf",
            chunkSequence: 0,
            errorsCorrected: 4,
            reVerificationPassed: true,
            verificationMethod: "AES-GCM AuthTag",
            details: "Recovered 4 bit flips via Reed-Solomon.");

        Assert.True(committed);
        Assert.NotNull(capturedEvent);
        Assert.Equal(testGuid, capturedEvent.FileGuid);
        Assert.True(capturedEvent.ReVerificationPassed);
        Assert.Equal(4, capturedEvent.SymbolErrorsCorrected);

        // Invalid repair where re-verification failed
        bool failedCommit = logger.AssertAndLogRepair(
            testGuid,
            "document.pdf",
            chunkSequence: 1,
            errorsCorrected: 10,
            reVerificationPassed: false,
            verificationMethod: "CRC32 Checksum",
            details: "Uncorrectable bit flips; checksum failed post-repair.");

        Assert.False(failedCommit);
        Assert.Equal(2, logger.GetEvents().Count);
    }
}
