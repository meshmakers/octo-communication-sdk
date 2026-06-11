namespace Meshmakers.Octo.Sdk.SimulationNodes;

internal static class Helpers
{
    public static ulong GetMaxValueForDigitCount(int digitCount)
    {
        if (digitCount < 1 || digitCount > 19)
        {
            throw new ArgumentOutOfRangeException(nameof(digitCount), "Allowed values are between 1 and 19");
        }

        if (digitCount == 19)
        {
            return long.MaxValue;
        }

        ulong result = 1;
        for (int i = 0; i < digitCount; i++)
        {
            result *= 10;
        }
        return result - 1;
    }
}