using NorthLife.Api.Models;

namespace NorthLife.Api.Recommendations;

/// <summary>The text of one event that the content model reads.</summary>
public sealed record ItemText(Guid Id, string Title, string Description, IReadOnlyList<string> Tags, EventCategory Category, string Locality);

/// <summary>A sparse, L2-normalised vector with sorted term indices.</summary>
public sealed class SparseVector(int[] indices, double[] values)
{
    public static SparseVector Empty { get; } = new([], []);

    public int[] Indices { get; } = indices;
    public double[] Values { get; } = values;

    /// <summary>Dot product by merging the two sorted index lists: O(|a| + |b|).</summary>
    public double Dot(SparseVector other)
    {
        double sum = 0;
        int i = 0, j = 0;
        while (i < Indices.Length && j < other.Indices.Length)
        {
            if (Indices[i] == other.Indices[j]) sum += Values[i++] * other.Values[j++];
            else if (Indices[i] < other.Indices[j]) i++;
            else j++;
        }

        return sum;
    }
}

/// <summary>
/// TF-IDF content model (Salton and Buckley, 1988). Each event becomes a weighted bag of terms: the
/// title counts twice, then tags, the description, and two special tokens for its category and
/// locality. Term frequency is sublinear (1 + ln tf) so a repeated word does not dominate, inverse
/// document frequency is ln((N + 1) / (df + 1)) + 1, and vectors are L2-normalised, so the cosine
/// similarity of two events is a plain dot product in [0, 1]. Words that appear in nearly every
/// description get an IDF near 1 and barely matter; rare, specific words ("ג׳אז", "קיאקים") drive
/// similarity. Building is O(total tokens).
/// </summary>
public sealed class TfIdfModel
{
    public const double TitleWeight = 2;
    public const double TagWeight = 1.5;
    public const double DescriptionWeight = 1;
    public const double FacetWeight = 1;

    private TfIdfModel(Dictionary<Guid, SparseVector> vectors, IReadOnlyList<string> vocabulary)
    {
        Vectors = vectors;
        Vocabulary = vocabulary;
    }

    public IReadOnlyDictionary<Guid, SparseVector> Vectors { get; }

    public IReadOnlyList<string> Vocabulary { get; }

    public double Cosine(Guid a, Guid b) =>
        Vectors.TryGetValue(a, out var left) && Vectors.TryGetValue(b, out var right) ? left.Dot(right) : 0;

    public static TfIdfModel Build(IReadOnlyList<ItemText> items)
    {
        // Pass 1: the corpus vocabulary guides prefix stripping.
        var raw = items.ToDictionary(item => item.Id, item => new
        {
            Title = HebrewText.Tokens(item.Title).ToList(),
            Tags = item.Tags.SelectMany(HebrewText.Tokens).ToList(),
            Description = HebrewText.Tokens(item.Description).ToList(),
        });
        var vocabulary = raw.Values
            .SelectMany(fields => fields.Title.Concat(fields.Tags).Concat(fields.Description))
            .ToHashSet(StringComparer.Ordinal);

        // Pass 2: weighted term frequencies per event.
        var termFrequencies = new Dictionary<Guid, Dictionary<string, double>>();
        foreach (var item in items)
        {
            var fields = raw[item.Id];
            var counts = new Dictionary<string, double>(StringComparer.Ordinal);
            void Add(string term, double weight) => counts[term] = counts.GetValueOrDefault(term) + weight;
            foreach (var token in fields.Title) Add(HebrewText.Stem(token, vocabulary), TitleWeight);
            foreach (var token in fields.Tags) Add(HebrewText.Stem(token, vocabulary), TagWeight);
            foreach (var token in fields.Description) Add(HebrewText.Stem(token, vocabulary), DescriptionWeight);
            Add($"#cat:{item.Category}", FacetWeight);
            Add($"#loc:{HebrewText.Normalize(item.Locality).Trim()}", FacetWeight);
            termFrequencies[item.Id] = counts;
        }

        var documentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var term in termFrequencies.Values.SelectMany(counts => counts.Keys)) documentFrequency[term] = documentFrequency.GetValueOrDefault(term) + 1;

        var terms = documentFrequency.Keys.Order(StringComparer.Ordinal).ToList();
        var index = terms.Select((term, position) => (term, position)).ToDictionary(pair => pair.term, pair => pair.position, StringComparer.Ordinal);
        var documents = items.Count;

        var vectors = new Dictionary<Guid, SparseVector>();
        foreach (var (id, counts) in termFrequencies)
        {
            var entries = counts
                .Select(pair => (Index: index[pair.Key], Weight: (1 + Math.Log(pair.Value)) * (Math.Log((documents + 1.0) / (documentFrequency[pair.Key] + 1)) + 1)))
                .OrderBy(entry => entry.Index)
                .ToArray();
            var norm = Math.Sqrt(entries.Sum(entry => entry.Weight * entry.Weight));
            vectors[id] = norm == 0
                ? SparseVector.Empty
                : new SparseVector(entries.Select(entry => entry.Index).ToArray(), entries.Select(entry => entry.Weight / norm).ToArray());
        }

        return new TfIdfModel(vectors, terms);
    }
}
