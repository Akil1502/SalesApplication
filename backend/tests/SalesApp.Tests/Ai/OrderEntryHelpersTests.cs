using System.Text;
using System.Text.Json;
using SalesApp.AI;
using SalesApp.AI.Claude;
using SalesApp.AI.OrderEntry;

namespace SalesApp.Tests.Ai;

public sealed class OrderFileTests
{
    [Theory]
    [InlineData("%PDF-1.7\n", OrderFileKind.Pdf, "application/pdf")]
    [InlineData("GIF89a....", OrderFileKind.Image, "image/gif")]
    [InlineData("RIFF\0\0\0\0WEBPVP8 ", OrderFileKind.Image, "image/webp")]
    public void Recognises_files_by_their_first_bytes(string header, OrderFileKind kind, string mediaType)
    {
        var type = OrderFile.Detect(Encoding.Latin1.GetBytes(header));

        Assert.Equal(new OrderFileType(kind, mediaType), type);
    }

    [Fact]
    public void Recognises_jpeg_and_png()
    {
        Assert.Equal("image/jpeg", OrderFile.Detect([0xFF, 0xD8, 0xFF, 0xE0, 0x00])?.MediaType);
        Assert.Equal("image/png", OrderFile.Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00])?.MediaType);
    }

    [Theory]
    [InlineData("PK\u0003\u0004 word document")] // .docx
    [InlineData("<html><script>")]
    [InlineData("")]
    public void Rejects_anything_else_whatever_it_is_called(string content)
    {
        Assert.Null(OrderFile.Detect(Encoding.Latin1.GetBytes(content)));
    }
}

public sealed class AliasTextTests
{
    [Theory]
    [InlineData("30S  Combed (white)!", "30s combed white")]
    [InlineData("  Cotton-Yarn 40's ", "cotton yarn 40 s")]
    [InlineData("வெள்ளை நூல்", "வெள்ளை நூல்")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalises_case_punctuation_and_spacing(string? input, string expected)
    {
        Assert.Equal(expected, AliasText.Normalize(input));
    }

    [Fact]
    public void Long_wording_is_capped()
    {
        Assert.Equal(AliasText.MaxLength, AliasText.Normalize(new string('a', 300)).Length);
    }
}

public sealed class AiCostTests
{
    private static readonly ModelPricing Opus = new() { Input = 4m, Output = 20m, CacheRead = 0.20m, CacheWrite = 5m };

    [Fact]
    public void Prices_each_kind_of_token_separately()
    {
        // 1,000 fresh input, 2,000 output, 10,000 read from cache, 3,000 written to it.
        var cost = AiCost.Estimate(Opus, input: 1_000, output: 2_000, cacheRead: 10_000, cacheWrite: 3_000);

        Assert.Equal(0.004m + 0.04m + 0.002m + 0.015m, cost);
    }

    [Fact]
    public void An_unpriced_model_costs_nothing_rather_than_failing()
    {
        Assert.Equal(0m, AiCost.Estimate(null, 1_000, 1_000, 0, 0));
    }
}

public sealed class OrderExtractionPromptTests
{
    [Fact]
    public void Catalog_is_ordered_by_sku_so_the_cached_prefix_is_stable()
    {
        CatalogEntry[] products =
        [
            new(2, "CY-40C", "Cotton yarn 40s combed", "Kg", "Cotton yarn", null, ["40s white"]),
            new(1, "CY-30C", "Cotton yarn 30s combed", "Kg", "Cotton yarn", "Ring spun,\ncompact", []),
        ];

        var forward = OrderExtractionPrompt.Catalog(products);
        var reversed = OrderExtractionPrompt.Catalog(products.Reverse());

        Assert.Equal(forward, reversed);
        Assert.True(forward.IndexOf("CY-30C", StringComparison.Ordinal) < forward.IndexOf("CY-40C", StringComparison.Ordinal));
        Assert.Contains("2 | CY-40C | Cotton yarn 40s combed | Kg | Cotton yarn |  | 40s white", forward, StringComparison.Ordinal);
        Assert.Contains("Ring spun, compact", forward, StringComparison.Ordinal);
    }

    [Fact]
    public void The_customers_text_is_fenced_and_dated()
    {
        var text = OrderExtractionPrompt.UserText(new DateOnly(2026, 10, 13), "  30s CC 500 kg  ", hasFile: false);

        Assert.StartsWith("Today's date (India): 2026-10-13 (Tuesday).", text, StringComparison.Ordinal);
        Assert.Contains("<customer_message>\n30s CC 500 kg\n</customer_message>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Schema_meets_structured_output_rules()
    {
        var schema = JsonSerializer.SerializeToElement(OrderExtractionPrompt.Schema);
        var objects = 0;

        void Walk(JsonElement node)
        {
            if (node.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (node.TryGetProperty("type", out var type))
            {
                // No nullable unions: structured outputs cap them, so absent values are "" or 0 instead.
                Assert.Equal(JsonValueKind.String, type.ValueKind);
                if (type.GetString() == "object")
                {
                    objects++;
                    Assert.False(node.GetProperty("additionalProperties").GetBoolean());
                    var properties = node.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order();
                    Assert.Equal(properties, node.GetProperty("required").EnumerateArray().Select(r => r.GetString()!).Order());
                }
            }

            foreach (var property in node.EnumerateObject())
            {
                Walk(property.Value);
            }
        }

        Walk(schema);
        Assert.Equal(3, objects); // the reply, its customer, and a line
    }
}

public sealed class ClaudeGatewayRequestTests
{
    private static readonly AiFeatureOptions Feature = new() { Model = "claude-opus-5-5", Effort = "low", MaxTokens = 4000 };

    [Fact]
    public void Only_the_marked_system_block_carries_the_cache_breakpoint()
    {
        var request = new ClaudeRequest(AiFeatures.OrderEntry,
            [new ClaudeSystemBlock("instructions"), new ClaudeSystemBlock("catalog", CacheAfter: true)],
            [new ClaudeText("order")]);

        // RawBodyData is the request body exactly as the SDK will send it.
        var json = JsonSerializer.SerializeToElement(ClaudeGateway.BuildParameters(request, Feature).RawBodyData);

        var system = json.GetProperty("system").EnumerateArray().ToList();
        Assert.Equal(2, system.Count);
        Assert.False(system[0].TryGetProperty("cache_control", out var none) && none.ValueKind != JsonValueKind.Null);
        Assert.Equal("ephemeral", system[1].GetProperty("cache_control").GetProperty("type").GetString());
        Assert.Equal("claude-opus-5-5", json.GetProperty("model").GetString());
        Assert.Equal(4000, json.GetProperty("max_tokens").GetInt32());
        Assert.Equal("low", json.GetProperty("output_config").GetProperty("effort").GetString());
    }

    [Fact]
    public void Files_go_before_the_text_and_the_schema_is_attached()
    {
        var request = new ClaudeRequest(AiFeatures.OrderEntry,
            [new ClaudeSystemBlock("instructions", CacheAfter: true)],
            [new ClaudeImage([1, 2, 3], "image/png"), new ClaudePdf([4, 5]), new ClaudeText("order")],
            OrderExtractionPrompt.Schema);

        // RawBodyData is the request body exactly as the SDK will send it.
        var json = JsonSerializer.SerializeToElement(ClaudeGateway.BuildParameters(request, Feature).RawBodyData);

        var content = json.GetProperty("messages")[0].GetProperty("content").EnumerateArray().ToList();
        Assert.Equal(["image", "document", "text"], content.Select(c => c.GetProperty("type").GetString()));
        Assert.Equal("image/png", content[0].GetProperty("source").GetProperty("media_type").GetString());
        Assert.Equal("AQID", content[0].GetProperty("source").GetProperty("data").GetString());
        Assert.Equal("application/pdf", content[1].GetProperty("source").GetProperty("media_type").GetString());

        var format = json.GetProperty("output_config").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("schema").GetProperty("properties").TryGetProperty("lines", out _));
    }
}
