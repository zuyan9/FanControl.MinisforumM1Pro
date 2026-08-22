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
    internal static void AssertBoard(M1ModelProfile profile, string board)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!string.Equals(board, profile.Board, StringComparison.Ordinal))
        {
            throw new PlatformNotSupportedException(
                $"Expected baseboard {profile.Board} for {profile.ModelName}; found {board}.");
        }
    }
}
