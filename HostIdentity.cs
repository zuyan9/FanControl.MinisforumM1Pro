using Microsoft.Win32;

namespace FanControl.MinisforumM1Pro;

internal static class HostIdentity
{
    private const string BiosKey =
        @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS";

    internal static string ReadBoard() => Convert.ToString(
        Registry.GetValue(BiosKey, "BaseBoardProduct", null))?.Trim() ?? string.Empty;
}

internal static class HostIdentityGate
{
    internal static void AssertBoard(string board)
    {
        if (!string.Equals(board, ArbscProfile.Board, StringComparison.Ordinal))
        {
            throw new PlatformNotSupportedException(
                $"Expected baseboard {ArbscProfile.Board}; found {board}.");
        }
    }
}
