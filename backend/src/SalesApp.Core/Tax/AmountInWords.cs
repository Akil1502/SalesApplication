using System.Globalization;
using System.Text;

namespace SalesApp.Core.Tax;

/// <summary>
/// Rupee amounts in words using the Indian numbering system (thousand, lakh, crore), as printed on
/// invoices: 123456.50 -> "Rupees One Lakh Twenty Three Thousand Four Hundred Fifty Six and Fifty Paise Only".
/// </summary>
public static class AmountInWords
{
    private static readonly string[] Ones =
    [
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten",
        "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen",
    ];

    private static readonly string[] Tens = ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

    public static string Rupees(decimal amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount can't be negative.");
        }

        var rounded = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        var rupees = (long)Math.Floor(rounded);
        var paise = (int)((rounded - rupees) * 100);

        var text = new StringBuilder("Rupees ").Append(Words(rupees));
        if (paise > 0)
        {
            text.Append(" and ").Append(Words(paise)).Append(" Paise");
        }

        return text.Append(" Only").ToString();
    }

    /// <summary>A whole number in words, Indian style: crore (10^7), lakh (10^5), thousand, hundred.</summary>
    public static string Words(long number)
    {
        if (number == 0)
        {
            return Ones[0];
        }

        var parts = new List<string>();
        // Above 99 crore, the crore count itself is spelled with lakh/thousand (e.g. "One Hundred Crore").
        if (number >= 10_000_000)
        {
            parts.Add($"{Words(number / 10_000_000)} Crore");
            number %= 10_000_000;
        }

        AddGroup(parts, ref number, 100_000, "Lakh");
        AddGroup(parts, ref number, 1_000, "Thousand");
        AddGroup(parts, ref number, 100, "Hundred");

        if (number > 0)
        {
            parts.Add(UnderHundred((int)number));
        }

        return string.Join(' ', parts);
    }

    private static void AddGroup(List<string> parts, ref long number, long unit, string name)
    {
        if (number >= unit)
        {
            parts.Add($"{UnderHundred((int)(number / unit))} {name}");
            number %= unit;
        }
    }

    private static string UnderHundred(int n) =>
        n < 20 ? Ones[n] : Tens[n / 10] + (n % 10 > 0 ? " " + Ones[n % 10] : string.Empty);

    // Tamil: 1–19 and the tens are single words; 21–99 join the ten and the unit the way they're written
    // (இருபத்தொன்று, இருபத்து மூன்று). A number ending in -உ that comes before ஆயிரம் drops the உ and takes the
    // ஆ (இரண்டாயிரம்); lakh and crore stay separate words. Larger groups end in their joining form when more follows
    // (ஆயிரத்து, லட்சத்து, கோடியே).
    private static readonly string[] TamilOnes =
    [
        "பூஜ்ஜியம்", "ஒன்று", "இரண்டு", "மூன்று", "நான்கு", "ஐந்து", "ஆறு", "ஏழு", "எட்டு", "ஒன்பது", "பத்து",
        "பதினொன்று", "பன்னிரண்டு", "பதின்மூன்று", "பதினான்கு", "பதினைந்து", "பதினாறு", "பதினேழு", "பதினெட்டு", "பத்தொன்பது",
    ];

    private static readonly string[] TamilTens = ["", "", "இருபது", "முப்பது", "நாற்பது", "ஐம்பது", "அறுபது", "எழுபது", "எண்பது", "தொண்ணூறு"];

    /// <summary>The tens before a unit, without their final உ: இருபத்த + ொன்று = இருபத்தொன்று.</summary>
    private static readonly string[] TamilTensStem = ["", "", "இருபத்த", "முப்பத்த", "நாற்பத்த", "ஐம்பத்த", "அறுபத்த", "எழுபத்த", "எண்பத்த", "தொண்ணூற்ற"];

    /// <summary>A unit joined onto a stem: the vowel sign that replaces its first letter, or "" to keep it a word.</summary>
    private static readonly string[] TamilUnitJoined = ["", "ொன்று", "ிரண்டு", "", "", "ைந்து", "ாறு", "ேழு", "ெட்டு", "ொன்பது"];

    private static readonly string[] TamilHundreds = ["", "நூறு", "இருநூறு", "முந்நூறு", "நானூறு", "ஐந்நூறு", "அறுநூறு", "எழுநூறு", "எண்ணூறு", "தொள்ளாயிரம்"];

    private static readonly string[] TamilHundredsJoining =
        ["", "நூற்று", "இருநூற்று", "முந்நூற்று", "நானூற்று", "ஐந்நூற்று", "அறுநூற்று", "எழுநூற்று", "எண்ணூற்று", "தொள்ளாயிரத்து"];

    private const char TamilU = 'ு';

    /// <summary>
    /// Rupee amounts in Tamil words for Tamil invoices: 12450 -> "ரூபாய் பன்னிரண்டாயிரத்து நானூற்று ஐம்பது மட்டும்".
    /// </summary>
    public static string TamilRupees(decimal amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount can't be negative.");
        }

        var rounded = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        var rupees = (long)Math.Floor(rounded);
        var paise = (int)((rounded - rupees) * 100);

        var text = new StringBuilder("ரூபாய் ").Append(TamilWords(rupees));
        if (paise > 0)
        {
            text.Append(" மற்றும் ").Append(TamilWords(paise)).Append(" பைசா");
        }

        return text.Append(" மட்டும்").ToString();
    }

    /// <summary>A whole number in Tamil words, Indian style: கோடி (10^7), லட்சம் (10^5), ஆயிரம், நூறு.</summary>
    public static string TamilWords(long number)
    {
        if (number == 0)
        {
            return TamilOnes[0];
        }

        var parts = new List<string>();
        var crores = number / 10_000_000;
        var lakhs = number % 10_000_000 / 100_000;
        var thousands = number % 100_000 / 1_000;
        var hundreds = number % 1_000 / 100;
        var rest = (int)(number % 100);

        if (crores > 0)
        {
            var more = number % 10_000_000 > 0;
            parts.Add($"{(crores == 1 ? "ஒரு" : TamilWords(crores))} {(more ? "கோடியே" : "கோடி")}");
        }

        if (lakhs > 0)
        {
            var more = number % 100_000 > 0;
            parts.Add($"{(lakhs == 1 ? "ஒரு" : TamilUnderHundred((int)lakhs))} {(more ? "லட்சத்து" : "லட்சம்")}");
        }

        if (thousands > 0)
        {
            var word = TamilThousands((int)thousands);
            parts.Add(number % 1_000 > 0 ? word[..^2] + "த்து" : word);
        }

        if (hundreds > 0)
        {
            parts.Add(rest > 0 ? TamilHundredsJoining[hundreds] : TamilHundreds[hundreds]);
        }

        if (rest > 0)
        {
            parts.Add(TamilUnderHundred(rest));
        }

        return string.Join(' ', parts);
    }

    private static string TamilUnderHundred(int n)
    {
        if (n < 20)
        {
            return TamilOnes[n];
        }

        var unit = n % 10;
        if (unit == 0)
        {
            return TamilTens[n / 10];
        }

        var joined = TamilUnitJoined[unit];
        return joined.Length > 0 ? TamilTensStem[n / 10] + joined : $"{TamilTensStem[n / 10]}{TamilU} {TamilOnes[unit]}";
    }

    /// <summary>1–99 thousand as one word: ஆயிரம், இரண்டாயிரம், பதினோராயிரம், இருபத்து மூன்றாயிரம்.</summary>
    private static string TamilThousands(int n)
    {
        if (n == 1)
        {
            return "ஆயிரம்";
        }

        // One before ஆயிரம் is ஓர்: பதினோராயிரம், இருபத்தோராயிரம்.
        if (n == 11)
        {
            return "பதினோராயிரம்";
        }

        if (n > 20 && n % 10 == 1)
        {
            return TamilTensStem[n / 10] + "ோராயிரம்";
        }

        var words = TamilUnderHundred(n);
        return words[^1] == TamilU ? words[..^1] + "ாயிரம்" : words + " ஆயிரம்";
    }

    /// <summary>Formats with Indian digit grouping: 1234567.5 -> "12,34,567.50".</summary>
    public static string IndianGrouping(decimal amount) =>
        amount.ToString("#,##,##0.00", CultureInfo.GetCultureInfo("en-IN"));
}
