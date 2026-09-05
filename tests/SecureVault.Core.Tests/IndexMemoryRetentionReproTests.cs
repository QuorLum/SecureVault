using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using SecureVault.Core;
using SecureVault.Core.Format;
using SecureVault.Core.Notes;
using Xunit;

namespace SecureVault.Core.Tests;

[Collection("NonParallelMemoryTests")]
public class IndexMemoryRetentionReproTests : IDisposable
{
    private readonly string _tempVaultPath;
    private readonly string _password = "TestPassword123!";

    public IndexMemoryRetentionReproTests()
    {
        _tempVaultPath = Path.Combine(Path.GetTempPath(), "mem_retention_" + Guid.NewGuid().ToString("N") + ".vault");
    }

    public void Dispose()
    {
        if (File.Exists(_tempVaultPath))
        {
            try { File.Delete(_tempVaultPath); } catch { }
        }
    }

    // XOR mask used to store needles so the unmasked pattern never appears in the scanner's own memory/stack/constants
    private const byte XorMask = 0xA5;

    private static readonly byte[] MaskedCanaryFileNameUtf8;
    private static readonly byte[] MaskedCanaryNoteBodyUtf8;

    static IndexMemoryRetentionReproTests()
    {
        char[] fileChars = new char[] { 'C','A','N','A','R','Y','_','F','I','L','E','N','A','M','E','_','7','f','3','a','9','b','.','t','x','t' };
        char[] noteChars = new char[] { 'C','A','N','A','R','Y','_','N','O','T','E','_','B','O','D','Y','_','2','e','8','c','1','d' };

        MaskedCanaryFileNameUtf8 = new byte[fileChars.Length];
        for (int i = 0; i < fileChars.Length; i++) MaskedCanaryFileNameUtf8[i] = (byte)((byte)fileChars[i] ^ XorMask);

        MaskedCanaryNoteBodyUtf8 = new byte[noteChars.Length];
        for (int i = 0; i < noteChars.Length; i++) MaskedCanaryNoteBodyUtf8[i] = (byte)((byte)noteChars[i] ^ XorMask);

        Array.Clear(fileChars, 0, fileChars.Length);
        Array.Clear(noteChars, 0, noteChars.Length);
    }

    private static string CreateCanaryString(byte[] maskedUtf8)
    {
        char[] chars = new char[maskedUtf8.Length];
        for (int i = 0; i < maskedUtf8.Length; i++)
        {
            chars[i] = (char)(maskedUtf8[i] ^ XorMask);
        }
        string s = new string(chars);
        Array.Clear(chars, 0, chars.Length);
        return s;
    }

    [Fact]
    public void StringWiping_DirectVerification_MutatesHeapCharactersToNull()
    {
        // Dynamic canary
        char[] canaryChars = new char[] { 'D','Y','N','A','M','I','C','_','C','A','N','A','R','Y','_','1','2','3' };
        string dynamicCanary = new string(canaryChars);
        byte[] dynamicMasked = new byte[canaryChars.Length];
        for (int i = 0; i < canaryChars.Length; i++) dynamicMasked[i] = (byte)((byte)canaryChars[i] ^ XorMask);

        Array.Clear(canaryChars, 0, canaryChars.Length);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Before wiping, pattern exists in heap (from dynamicCanary)
        int countBefore = MemoryScanner.CountMaskedPatternInHeap(dynamicMasked, XorMask, isUtf16: true);
        Assert.True(countBefore >= 1, "Dynamic canary string must exist in heap before wiping.");

        // Wipe string in place
        VaultIndex.WipeString(dynamicCanary);
        Assert.Equal('\0', dynamicCanary[0]);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // After wiping, pattern is completely gone from private heap
        int countAfter = MemoryScanner.CountMaskedPatternInHeap(dynamicMasked, XorMask, isUtf16: true);
        Assert.Equal(0, countAfter);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private async Task CreateAndPopulateVaultAsync()
    {
        var (setupVault, _) = await VaultManager.CreateAsync(_tempVaultPath, _password);
        using (setupVault)
        {
            string initFilename = CreateCanaryString(MaskedCanaryFileNameUtf8);
            string initNote = CreateCanaryString(MaskedCanaryNoteBodyUtf8);

            using var ms = new MemoryStream(Encoding.UTF8.GetBytes("Canary file payload content"));
            var entry = await setupVault.AddFileAsync(ms, initFilename, "/Canary");
            entry.Notes = initNote;
            setupVault.PersistIndexAndFooter();

            VaultIndex.WipeString(initFilename);
            VaultIndex.WipeString(initNote);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private async Task UnlockAndLockVaultAsync()
    {
        var unlockedVault = await VaultManager.OpenAsync(_tempVaultPath, _password);
        Assert.NotEmpty(unlockedVault.Files);
        unlockedVault.Lock();
    }

    [Fact]
    public async Task Patched_Lock_ZeroesAndClearsIndex_LeavesZeroCanariesInProcessMemory()
    {
        // P-03 REQUIREMENT:
        // "unlock a vault with a canary filename CANARY_FILENAME_7f3a9b.txt and a canary note body CANARY_NOTE_BODY_2e8c1d,
        //  lock, dump process memory, search for both canaries. Must find zero occurrences."

        // 1. Create and populate vault
        await CreateAndPopulateVaultAsync();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // 2. Unlock & Lock vault in non-inlined method so stack frames are completely popped
        await UnlockAndLockVaultAsync();

        // 3. Force GC collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // 4. Scan process memory for both canaries (UTF-16 and UTF-8 representations)
        int fileU16 = MemoryScanner.CountMaskedPatternInHeap(MaskedCanaryFileNameUtf8, XorMask, isUtf16: true);
        int fileU8 = MemoryScanner.CountMaskedPatternInHeap(MaskedCanaryFileNameUtf8, XorMask, isUtf16: false);
        int noteU16 = MemoryScanner.CountMaskedPatternInHeap(MaskedCanaryNoteBodyUtf8, XorMask, isUtf16: true);
        int noteU8 = MemoryScanner.CountMaskedPatternInHeap(MaskedCanaryNoteBodyUtf8, XorMask, isUtf16: false);

        Assert.True(fileU16 == 0 && fileU8 == 0 && noteU16 == 0 && noteU8 == 0,
            $"Memory retention failure: FileU16={fileU16}, FileU8={fileU8}, NoteU16={noteU16}, NoteU8={noteU8}");
    }

    private static class MemoryScanner
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, uint dwLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public ushort PartitionId;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        private const uint MEM_COMMIT = 0x1000;
        private const uint PAGE_READWRITE = 0x04;
        private const uint PAGE_EXECUTE_READWRITE = 0x40;
        private const uint MEM_PRIVATE = 0x20000;
        private const int ChunkSize = 65536;

        public static unsafe int CountMaskedPatternInHeap(byte[] maskedPattern, byte xorMask, bool isUtf16)
        {
            int patternLen = isUtf16 ? maskedPattern.Length * 2 : maskedPattern.Length;

            IntPtr hProcess = GetCurrentProcess();
            IntPtr address = IntPtr.Zero;
            int totalOccurrences = 0;
            uint mbiSize = (uint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();

            IntPtr unmanagedBuf = Marshal.AllocHGlobal(ChunkSize);
            try
            {
                while (VirtualQueryEx(hProcess, address, out MEMORY_BASIC_INFORMATION mbi, mbiSize) != 0)
                {
                    if (mbi.State == MEM_COMMIT && mbi.Type == MEM_PRIVATE &&
                        (mbi.Protect == PAGE_READWRITE || mbi.Protect == PAGE_EXECUTE_READWRITE))
                    {
                        long baseAddr = mbi.BaseAddress.ToInt64();
                        long bufAddr = unmanagedBuf.ToInt64();
                        if (bufAddr >= baseAddr && bufAddr < baseAddr + mbi.RegionSize.ToInt64())
                        {
                            long next = baseAddr + mbi.RegionSize.ToInt64();
                            address = new IntPtr(next);
                            continue;
                        }

                        long regionRemaining = (long)mbi.RegionSize;
                        long regionOffset = 0;

                        while (regionRemaining > 0)
                        {
                            int toRead = (int)Math.Min(regionRemaining, ChunkSize);
                            IntPtr readAddr = new IntPtr(baseAddr + regionOffset);

                            if (ReadProcessMemory(hProcess, readAddr, unmanagedBuf, toRead, out IntPtr bytesRead) && (int)bytesRead >= patternLen)
                            {
                                byte* bufPtr = (byte*)unmanagedBuf;
                                int bytesCount = (int)bytesRead;
                                int searchIdx = 0;

                                while (searchIdx <= bytesCount - patternLen)
                                {
                                    bool match = true;
                                    for (int k = 0; k < maskedPattern.Length; k++)
                                    {
                                        byte expected = (byte)(maskedPattern[k] ^ xorMask);
                                        if (isUtf16)
                                        {
                                            if (bufPtr[searchIdx + k * 2] != expected || bufPtr[searchIdx + k * 2 + 1] != 0)
                                            {
                                                match = false;
                                                break;
                                            }
                                        }
                                        else
                                        {
                                            if (bufPtr[searchIdx + k] != expected)
                                            {
                                                match = false;
                                                break;
                                            }
                                        }
                                    }

                                    if (match)
                                    {
                                        totalOccurrences++;
                                        searchIdx += patternLen;
                                    }
                                    else
                                    {
                                        searchIdx++;
                                    }
                                }

                                new Span<byte>((void*)unmanagedBuf, (int)bytesRead).Clear();
                            }

                            regionOffset += toRead;
                            regionRemaining -= toRead;
                        }
                    }

                    long nextAddr = mbi.BaseAddress.ToInt64() + mbi.RegionSize.ToInt64();
                    if (nextAddr <= address.ToInt64()) break;
                    address = new IntPtr(nextAddr);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(unmanagedBuf);
            }

            return totalOccurrences;
        }
    }
}
