using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Contracts.Ai;
using SalesApp.Core.Validation;
using SalesApp.Infrastructure.Data;

namespace SalesApp.AI.OrderEntry;

/// <summary>A customer as the matcher sees it.</summary>
public sealed record CustomerRecord(int Id, string Code, string Name, string? TradeName, string City, string? Phone, string? Gstin);

/// <summary>
/// Finds the customer an order came from using rules, not the AI: an exact GSTIN first, then the phone number, then
/// a fuzzy name match. The model only reports what the message says; it never sees the customer list.
/// </summary>
public sealed class CustomerMatcher(AppDbContext db)
{
    /// <summary>A name match this close, clearly ahead of the next one, is accepted without asking.</summary>
    internal const decimal AutoMatchScore = 0.85m;
    internal const decimal CandidateScore = 0.45m;

    public async Task<CustomerSuggestionDto> MatchAsync(CustomerCluesDto clues, int? selectedCustomerId, CancellationToken cancellationToken)
    {
        var customers = await db.Customers.AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => new CustomerRecord(c.Id, c.Code, c.Name, c.TradeName, c.BillingAddress.City, c.Phone, c.Gstin))
            .ToListAsync(cancellationToken);
        return Match(customers, clues, selectedCustomerId);
    }

    internal static CustomerSuggestionDto Match(IReadOnlyList<CustomerRecord> customers, CustomerCluesDto clues, int? selectedCustomerId)
    {
        var candidates = RankByName(customers, clues);

        if (selectedCustomerId is { } selectedId && customers.FirstOrDefault(c => c.Id == selectedId) is { } selected)
        {
            return Suggest(selected, CustomerMatchSource.Selected, clues, candidates);
        }

        if (!string.IsNullOrWhiteSpace(clues.Gstin) && Gstin.IsValid(clues.Gstin))
        {
            var gstin = Gstin.Normalize(clues.Gstin);
            if (customers.FirstOrDefault(c => c.Gstin == gstin) is { } byGstin)
            {
                return Suggest(byGstin, CustomerMatchSource.Gstin, clues, candidates);
            }
        }

        var phone = PhoneKey(clues.Phone);
        if (phone is not null && customers.Where(c => PhoneKey(c.Phone) == phone).ToList() is [var byPhone])
        {
            return Suggest(byPhone, CustomerMatchSource.Phone, clues, candidates);
        }

        if (candidates is [var best, ..] && best.Score >= AutoMatchScore && (candidates.Count == 1 || candidates[1].Score <= best.Score - 0.1m))
        {
            return Suggest(customers.First(c => c.Id == best.Id), CustomerMatchSource.Name, clues, candidates);
        }

        return new CustomerSuggestionDto(null, null, CustomerMatchSource.None, clues, candidates);
    }

    private static CustomerSuggestionDto Suggest(CustomerRecord customer, CustomerMatchSource source, CustomerCluesDto clues, IReadOnlyList<CustomerCandidateDto> candidates) =>
        new(customer.Id, customer.Name, source, clues, candidates);

    private static List<CustomerCandidateDto> RankByName(IReadOnlyList<CustomerRecord> customers, CustomerCluesDto clues)
    {
        var clue = NameKey(clues.Name);
        if (clue.Length == 0)
        {
            return [];
        }

        var city = clues.City?.Trim();
        return customers
            .Select(c =>
            {
                var score = Math.Max(Similarity(clue, NameKey(c.Name)), Similarity(clue, NameKey(c.TradeName)));
                if (score > 0 && !string.IsNullOrEmpty(city) && string.Equals(city, c.City, StringComparison.OrdinalIgnoreCase))
                {
                    score = Math.Min(1m, score + 0.05m);
                }

                return new CustomerCandidateDto(c.Id, c.Code, c.Name, c.City, Math.Round(score, 2));
            })
            .Where(c => c.Score >= CandidateScore)
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
    }

    private static readonly HashSet<string> NameNoise = new(StringComparer.Ordinal)
    {
        "m", "s", "ms", "pvt", "private", "ltd", "limited", "llp", "co", "company", "and", "the",
    };

    /// <summary>Business-name words that tell nothing apart ("Pvt", "Ltd", "M/s") are dropped before comparing.</summary>
    internal static string NameKey(string? name)
    {
        var words = AliasText.Normalize(name).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !NameNoise.Contains(w));
        return string.Join(' ', words);
    }

    /// <summary>Dice coefficient over character pairs: 1 for identical names, near 0 for unrelated ones.</summary>
    internal static decimal Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0m;
        }

        if (a == b)
        {
            return 1m;
        }

        var pairsA = Bigrams(a);
        var pairsB = Bigrams(b);
        var common = 0;
        foreach (var (pair, count) in pairsA)
        {
            common += Math.Min(count, pairsB.GetValueOrDefault(pair));
        }

        var total = pairsA.Values.Sum() + pairsB.Values.Sum();
        return total == 0 ? 0m : 2m * common / total;
    }

    private static Dictionary<string, int> Bigrams(string value)
    {
        var pairs = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < value.Length - 1; i++)
        {
            var pair = value.Substring(i, 2);
            pairs[pair] = pairs.GetValueOrDefault(pair) + 1;
        }

        return pairs;
    }

    /// <summary>The last ten digits, so "+91 98400 12345", "098400-12345" and "9840012345" all compare equal.</summary>
    internal static string? PhoneKey(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        return digits.Length >= 10 ? digits[^10..] : null;
    }
}
