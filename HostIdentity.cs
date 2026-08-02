using System.Management;

namespace FanControl.MinisforumM1Pro;

internal sealed record HostIdentity(string Manufacturer, string Product, string Board);

internal interface IHostIdentityReader
{
    HostIdentity Read();
}

internal sealed class WmiHostIdentityReader : IHostIdentityReader
{
    public HostIdentity Read()
    {
        using ManagementObject system = ReadOne(
            "SELECT Manufacturer, Model FROM Win32_ComputerSystem");
        using ManagementObject board = ReadOne(
            "SELECT Product FROM Win32_BaseBoard");
        return new HostIdentity(
            Convert.ToString(system["Manufacturer"])?.Trim() ?? string.Empty,
            Convert.ToString(system["Model"])?.Trim() ?? string.Empty,
            Convert.ToString(board["Product"])?.Trim() ?? string.Empty);
    }

    private static ManagementObject ReadOne(string query)
    {
        using ManagementObjectSearcher searcher = new(query);
        using ManagementObjectCollection results = searcher.Get();
        return results.Cast<ManagementObject>().FirstOrDefault() ??
            throw new InvalidOperationException($"WMI query returned no result: {query}");
    }
}

internal static class HostIdentityGate
{
    internal static void AssertExact(HostIdentity host)
    {
        if (host.Manufacturer != ArbscProfile.Manufacturer ||
            host.Product != ArbscProfile.Product ||
            host.Board != ArbscProfile.Board)
        {
            throw new PlatformNotSupportedException(
                $"Expected {ArbscProfile.Product}/{ArbscProfile.Board}; found " +
                $"{host.Product}/{host.Board}.");
        }
    }
}
