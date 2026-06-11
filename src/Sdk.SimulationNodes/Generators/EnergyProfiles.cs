namespace Meshmakers.Octo.Sdk.SimulationNodes.Generators;

/// <summary>
/// Pure, deterministic load- and PV-profile math reused by the <see cref="LoadProfileGenerator"/>,
/// <see cref="PvProfileGenerator"/> and pipeline nodes that need the same per-slot energy curves
/// without going through a simulator key. Profiles are simplified synthetic shapes that
/// approximate the BDEW H0/G0/L0 daytime profile families and a clipped-sine PV curve — sufficient
/// for end-to-end pipeline-validation simulations; not a substitute for real BDEW data.
/// </summary>
public static class EnergyProfiles
{
    /// <summary>
    /// 96-element table per profile key (H0, G0, L0). Each entry holds the share of the daily
    /// energy delivered in that 15-min slot; the values sum to 1.
    /// </summary>
    public static IReadOnlyDictionary<string, double[]> LoadProfileWeights { get; } = Build();

    /// <summary>
    /// Returns the energy in the slot <paramref name="slotIndex"/> (0..95) of a day with total
    /// energy <paramref name="dailyEnergyKwh"/> for the given <paramref name="profileKey"/>
    /// (H0/G0/L0, case-insensitive).
    /// </summary>
    public static double LoadProfileSlot(string profileKey, double dailyEnergyKwh, int slotIndex)
    {
        var key = profileKey.ToUpperInvariant();
        if (!LoadProfileWeights.TryGetValue(key, out var weights))
        {
            throw new ArgumentException(
                $"Unknown load profile '{profileKey}'. Supported: H0, G0, L0.", nameof(profileKey));
        }

        if (slotIndex < 0 || slotIndex >= weights.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex),
                $"slotIndex {slotIndex} out of range [0, {weights.Length}).");
        }

        return weights[slotIndex] * dailyEnergyKwh;
    }

    /// <summary>
    /// Returns the PV energy in the slot <paramref name="slotIndex"/> (0..95) for an installation
    /// with peak power <paramref name="peakKwp"/> on day <paramref name="dayOfYear"/> (1..366).
    /// </summary>
    public static double PvProfileSlot(double peakKwp, int dayOfYear, int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= 96)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex),
                $"slotIndex {slotIndex} out of range [0, 96).");
        }

        var seasonRad = 2.0 * Math.PI * (dayOfYear - 172) / 365.25;
        var daylightHours = 12.0 + 4.0 * Math.Cos(seasonRad);
        var halfDaylight = daylightHours / 2.0;

        var slotHourOffset = (slotIndex - 48) / 4.0;          // solar noon at UTC 12:00 = slot 48
        if (Math.Abs(slotHourOffset) >= halfDaylight)
        {
            return 0.0;
        }

        var instantaneousPower = peakKwp * Math.Cos(Math.PI * slotHourOffset / daylightHours);
        if (instantaneousPower < 0)
        {
            instantaneousPower = 0;
        }

        return instantaneousPower * 0.25;                     // 15 min = 0.25 h
    }

    private static IReadOnlyDictionary<string, double[]> Build()
    {
        return new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["H0"] = NormaliseToOne(BuildH0()),
            ["G0"] = NormaliseToOne(BuildG0()),
            ["L0"] = NormaliseToOne(BuildL0()),
        };
    }

    private static double[] BuildH0()
    {
        // Household: low at night, breakfast bump 07:00–08:00, midday bump 12:00–13:00,
        // evening peak 18:00–20:00, gentle night dip 00:00–06:00.
        var w = new double[96];
        for (var i = 0; i < 96; i++) w[i] = 0.3;
        for (var i = 28; i <= 32; i++) w[i] += 0.6;
        for (var i = 48; i <= 52; i++) w[i] += 0.3;
        for (var i = 72; i <= 80; i++) w[i] += 1.4;
        for (var i = 0; i < 24; i++) w[i] -= 0.15;
        return w;
    }

    private static double[] BuildG0()
    {
        // Commercial: broad office-hours plateau 08:00–18:00, low otherwise.
        var w = new double[96];
        for (var i = 0; i < 96; i++) w[i] = 0.15;
        for (var i = 32; i <= 72; i++) w[i] += 1.1;
        return w;
    }

    private static double[] BuildL0()
    {
        // Agriculture: early-morning + evening peaks (milking, feeding), low midday.
        var w = new double[96];
        for (var i = 0; i < 96; i++) w[i] = 0.25;
        for (var i = 20; i <= 28; i++) w[i] += 0.9;
        for (var i = 64; i <= 76; i++) w[i] += 0.9;
        return w;
    }

    private static double[] NormaliseToOne(double[] weights)
    {
        var sum = 0.0;
        foreach (var v in weights) sum += v;
        if (sum <= 0) return weights;
        var result = new double[weights.Length];
        for (var i = 0; i < weights.Length; i++) result[i] = weights[i] / sum;
        return result;
    }
}
