namespace BoardGameMondays.Core;

/// <summary>
/// Formats for displaying odds.
/// </summary>
public enum OddsDisplayFormat
{
    /// <summary>
    /// Fractional odds (e.g., 3/4, 5/2).
    /// </summary>
    Fraction = 0,

    /// <summary>
    /// Decimal odds (e.g., 1.75, 3.50).
    /// </summary>
    Decimal = 1
}

/// <summary>
/// Centralized service for formatting betting odds consistently across the app.
/// </summary>
public static class OddsFormatter
{
    private const int MaxFractionDenominator = 20;
    private const int MaxFractionNumerator = 99;
    private const int MaxLongshotNumerator = 100;
    /// <summary>
    /// Formats odds based on the specified display format.
    /// </summary>
    /// <param name="oddsTimes100">The odds multiplied by 100 (e.g., 175 = 1.75 decimal odds).</param>
    /// <param name="format">The display format to use.</param>
    /// <returns>A formatted string representation of the odds.</returns>
    public static string Format(int oddsTimes100, OddsDisplayFormat format)
    {
        return format switch
        {
            OddsDisplayFormat.Decimal => FormatDecimal(oddsTimes100),
            OddsDisplayFormat.Fraction => FormatFraction(oddsTimes100),
            _ => FormatFraction(oddsTimes100)
        };
    }

    /// <summary>
    /// Formats odds based on the specified display format, handling nullable values.
    /// </summary>
    /// <param name="oddsTimes100">The odds multiplied by 100, or null.</param>
    /// <param name="format">The display format to use.</param>
    /// <returns>A formatted string representation of the odds, or "—" if null.</returns>
    public static string Format(int? oddsTimes100, OddsDisplayFormat format)
    {
        return oddsTimes100 is null ? "—" : Format(oddsTimes100.Value, format);
    }

    /// <summary>
    /// Formats odds as a decimal (e.g., 1.75).
    /// </summary>
    public static string FormatDecimal(int oddsTimes100)
    {
        var decimalOdds = oddsTimes100 / 100.0;
        return decimalOdds.ToString("0.00");
    }

    /// <summary>
    /// Formats odds as a decimal, handling nullable values.
    /// </summary>
    public static string FormatDecimal(int? oddsTimes100)
    {
        return oddsTimes100 is null ? "—" : FormatDecimal(oddsTimes100.Value);
    }

    /// <summary>
    /// Formats odds as a fraction (e.g., 3/4).
    /// </summary>
    public static string FormatFraction(int oddsTimes100)
    {
        if (oddsTimes100 <= 100)
        {
            return "1/1";
        }

        var fractionalOdds = (oddsTimes100 - 100) / 100.0;
        var (numerator, denominator) = ApproximateFraction(fractionalOdds);
        return $"{numerator}/{denominator}";
    }

    /// <summary>
    /// Formats odds as a fraction, handling nullable values.
    /// </summary>
    public static string FormatFraction(int? oddsTimes100)
    {
        return oddsTimes100 is null ? "—" : FormatFraction(oddsTimes100.Value);
    }

    /// <summary>
    /// Converts odds to a fraction tuple for calculations.
    /// </summary>
    public static (int Numerator, int Denominator)? ToFraction(int? oddsTimes100)
    {
        if (oddsTimes100 is null || oddsTimes100 <= 100)
        {
            return oddsTimes100 <= 100 && oddsTimes100 is not null ? (1, 1) : null;
        }

        var numerator = oddsTimes100.Value - 100;
        var denominator = 100;
        var gcd = Gcd(numerator, denominator);
        return (numerator / gcd, denominator / gcd);
    }

    /// <summary>
    /// Computes potential profit from a bet.
    /// </summary>
    public static int? ComputeProfit(int amount, int? oddsTimes100)
    {
        var f = ToFraction(oddsTimes100);
        if (f is null)
        {
            return null;
        }

        return (amount * f.Value.Numerator) / f.Value.Denominator;
    }

    private static int Gcd(int a, int b)
    {
        a = Math.Abs(a);
        b = Math.Abs(b);
        while (b != 0)
        {
            var t = a % b;
            a = b;
            b = t;
        }
        return a == 0 ? 1 : a;
    }

    private static (int Numerator, int Denominator) ApproximateFraction(double value)
    {
        if (value <= 0)
        {
            return (1, 1);
        }

        if (value >= MaxFractionNumerator)
        {
            var rounded = (int)Math.Round(value);
            var capped = Math.Min(Math.Max(1, rounded), MaxLongshotNumerator);
            return (capped, 1);
        }

        var bestNumerator = 1;
        var bestDenominator = 1;
        var bestError = double.MaxValue;

        for (var denominator = 1; denominator <= MaxFractionDenominator; denominator++)
        {
            var numerator = (int)Math.Round(value * denominator);
            if (numerator < 1)
            {
                numerator = 1;
            }

            if (numerator > MaxFractionNumerator && denominator != 1)
            {
                continue;
            }

            var error = Math.Abs(value - (double)numerator / denominator);
            if (error < bestError - 1e-9 || (Math.Abs(error - bestError) < 1e-9 && denominator < bestDenominator))
            {
                bestNumerator = numerator;
                bestDenominator = denominator;
                bestError = error;
            }
        }

        var gcd = Gcd(bestNumerator, bestDenominator);
        return (bestNumerator / gcd, bestDenominator / gcd);
    }
}
