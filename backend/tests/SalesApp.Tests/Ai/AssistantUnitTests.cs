using System.Text.Json;
using Anthropic.Models.Messages;
using SalesApp.AI;
using SalesApp.AI.Assistant;
using SalesApp.AI.Claude;

namespace SalesApp.Tests.Ai;

public sealed class ClaudeStreamAccumulatorTests
{
    private static RawMessageStreamEvent Event(string json) => JsonSerializer.Deserialize<RawMessageStreamEvent>(json)!;

    /// <summary>A recorded stream: reasoning (signature only, as Opus 5.5 sends it), a sentence, then a tool call whose input arrives in pieces.</summary>
    private static readonly string[] Recorded =
    [
        """{"type":"message_start","message":{"id":"msg_01","type":"message","role":"assistant","model":"claude-opus-5-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":12,"cache_creation_input_tokens":250,"cache_read_input_tokens":4100,"output_tokens":1}}}""",
        """{"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":"","signature":""}}""",
        """{"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"EqQBCkgIBhABGAIiQL"}}""",
        """{"type":"content_block_stop","index":0}""",
        """{"type":"content_block_start","index":1,"content_block":{"type":"text","text":""}}""",
        """{"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"Let me check "}}""",
        """{"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"their invoices."}}""",
        """{"type":"content_block_stop","index":1}""",
        """{"type":"content_block_start","index":2,"content_block":{"type":"tool_use","id":"toolu_01","name":"list_invoices","input":{}}}""",
        """{"type":"content_block_delta","index":2,"delta":{"type":"input_json_delta","partial_json":""}}""",
        """{"type":"content_block_delta","index":2,"delta":{"type":"input_json_delta","partial_json":"{\"customerId\": 4"}}""",
        """{"type":"content_block_delta","index":2,"delta":{"type":"input_json_delta","partial_json":"2, \"status\": \"ove"}}""",
        """{"type":"content_block_delta","index":2,"delta":{"type":"input_json_delta","partial_json":"rdue\"}"}}""",
        """{"type":"content_block_stop","index":2}""",
        """{"type":"message_delta","delta":{"stop_reason":"tool_use","stop_sequence":null},"usage":{"output_tokens":87}}""",
        """{"type":"message_stop"}""",
    ];

    [Fact]
    public void Rebuilds_text_reasoning_and_tool_input_from_the_stream()
    {
        var stream = new ClaudeStreamAccumulator();
        var shown = Recorded.Select(e => stream.Add(Event(e))).OfType<ClaudeStreamEvent>().ToList();

        Assert.Equal(
            [new ClaudeThinkingStarted(), new ClaudeTextDelta("Let me check "), new ClaudeTextDelta("their invoices."), new ClaudeToolCallStarted("toolu_01", "list_invoices")],
            shown);

        var blocks = stream.Blocks();
        Assert.Equal(3, blocks.Count);
        Assert.Equal(new ClaudeThinkingBlock("", "EqQBCkgIBhABGAIiQL"), blocks[0]);
        Assert.Equal(new ClaudeTextBlock("Let me check their invoices."), blocks[1]);
        var call = Assert.IsType<ClaudeToolUseBlock>(blocks[2]);
        Assert.Equal(("toolu_01", "list_invoices"), (call.Id, call.Name));
        Assert.Equal(42, call.Input["customerId"].GetInt32());
        Assert.Equal("overdue", call.Input["status"].GetString());

        Assert.Equal("Let me check their invoices.", stream.Text);
        Assert.Equal(("msg_01", "tool_use"), (stream.MessageId, stream.StopReason));
        Assert.Equal((12L, 87L, 4100L, 250L), (stream.InputTokens, stream.OutputTokens, stream.CacheReadTokens, stream.CacheWriteTokens));
    }

    [Fact]
    public void A_tool_call_without_input_gets_an_empty_object()
    {
        var stream = new ClaudeStreamAccumulator();
        stream.Add(Event("""{"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"toolu_02","name":"search_products","input":{}}}"""));

        Assert.Empty(Assert.IsType<ClaudeToolUseBlock>(Assert.Single(stream.Blocks())).Input);
    }

    [Fact]
    public void An_incomplete_tool_input_is_an_error_not_a_guess()
    {
        var stream = new ClaudeStreamAccumulator();
        stream.Add(Event("""{"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"toolu_03","name":"get_customer","input":{}}}"""));
        stream.Add(Event("""{"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"customerId\": 4"}}"""));

        Assert.Throws<AiServiceException>(() => stream.Blocks());
    }

    [Fact]
    public void Redacted_reasoning_is_kept_for_the_next_turn()
    {
        var stream = new ClaudeStreamAccumulator();
        stream.Add(Event("""{"type":"content_block_start","index":0,"content_block":{"type":"redacted_thinking","data":"EmwKAhgBEgy3va3pzix"}}"""));

        Assert.Equal(new ClaudeRedactedThinkingBlock("EmwKAhgBEgy3va3pzix"), Assert.Single(stream.Blocks()));
    }
}

public sealed class ClaudeBlockJsonTests
{
    [Fact]
    public void Every_block_type_round_trips_unchanged()
    {
        var input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"customerId":42,"amount":1250.50,"filters":{"status":["overdue"]}}""")!;
        IReadOnlyList<ClaudeBlock> blocks =
        [
            new ClaudeTextBlock("<context>Today's date in India: 2026-10-14 (Wednesday).</context>"),
            new ClaudeThinkingBlock("", "EqQBCkgIBhABGAIiQL+/=="),
            new ClaudeRedactedThinkingBlock("EmwKAhgBEgy3va3pzix"),
            new ClaudeToolUseBlock("toolu_01", "list_invoices", input),
            new ClaudeToolResultBlock("toolu_01", "{\"count\":2}"),
            new ClaudeToolResultBlock("toolu_02", "No customer has id 9.", IsError: true),
            new ClaudeTextBlock("தமிழ் ₹1,87,17,602.50"),
        ];

        var json = ClaudeBlockJson.Serialize(blocks);
        var back = ClaudeBlockJson.Deserialize(json);

        Assert.Equal(json, ClaudeBlockJson.Serialize(back));
        Assert.Equal(blocks.Select(b => b.GetType()), back.Select(b => b.GetType()));
        Assert.Equal("1250.50", ((ClaudeToolUseBlock)back[3]).Input["amount"].GetRawText()); // numbers keep their exact form
        Assert.Equal(blocks[1], back[1]);
        Assert.Equal(blocks[6], back[6]);
        Assert.Contains("\"type\":\"tool_use\"", json, StringComparison.Ordinal);
    }
}

public sealed class DraftCheckerTests
{
    [Fact]
    public void Finds_document_numbers()
    {
        var numbers = DraftChecker.DocumentNumbers("Invoices INV/26-27/00042 and INV/26-27/00107, quotation QT/25-26/00003. Not INV-26-27-1 or 26-27/00042.");

        Assert.Equal(["INV/26-27/00042", "INV/26-27/00107", "QT/25-26/00003"], numbers);
    }

    [Theory]
    [InlineData("Please pay ₹1,87,17,602.50 today.", 18717602.50)]
    [InlineData("Balance Rs. 23,600 pending", 23600)]
    [InlineData("Balance Rs 2,360.00", 2360)]
    [InlineData("INR 12,345.6 due", 12345.6)]
    [InlineData("Total: ₹ 4,500", 4500)]
    public void Reads_exact_rupee_amounts(string text, double expected)
    {
        var amount = Assert.Single(DraftChecker.ExactAmounts(text));
        Assert.Equal((decimal)expected, amount.Value);
    }

    [Theory]
    [InlineData("Sales crossed ₹1.87 crore this year.")]
    [InlineData("Around ₹14.5 lakh is overdue.")]
    [InlineData("About Rs 3 lakhs.")]
    [InlineData("No amounts here, just 42 bales.")]
    public void Leaves_rounded_figures_and_plain_numbers_alone(string text) =>
        Assert.Empty(DraftChecker.ExactAmounts(text));

    [Fact]
    public void Flags_amounts_that_no_lookup_returned()
    {
        var known = DraftChecker.NumbersIn(["""{"invoices":[{"total":23600.00,"outstanding":11800.5}],"totalOutstanding":11800.50}""", "Not JSON at all"]);

        var unknown = DraftChecker.UnknownAmounts("Invoice total ₹23,600.00, of which ₹11,800.50 is outstanding. Pay ₹11,800 by Friday.", known);

        Assert.Equal(["₹11,800"], unknown);
    }
}

public sealed class AssistantPromptTests
{
    [Fact]
    public void The_system_prompt_is_the_same_every_day()
    {
        var first = AssistantPrompt.System("Demo Textiles", "Meena Krishnan", "sales manager", manager: true);
        var second = AssistantPrompt.System("Demo Textiles", "Meena Krishnan", "sales manager", manager: true);

        Assert.Equal(first, second);
        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}", first); // the date goes in each user message instead
        Assert.Contains("rpt_mgr.Margins", first, StringComparison.Ordinal);
        Assert.Contains("Meena Krishnan, Demo Textiles", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Users_without_cost_prices_are_told_they_cant_see_margins()
    {
        var rep = AssistantPrompt.System("Demo Textiles", "Arun Kumar", "Sales representative", manager: false);

        Assert.DoesNotContain("rpt_mgr", rep, StringComparison.Ordinal);
        Assert.Contains("can't see cost prices or margins", rep, StringComparison.Ordinal);
    }

    [Fact]
    public void Tools_are_well_formed()
    {
        Assert.Equal(AssistantPrompt.Tools.Count, AssistantPrompt.Tools.Select(t => t.Name).Distinct().Count());
        Assert.All(AssistantPrompt.Tools, tool =>
        {
            Assert.Matches("^[a-z_]+$", tool.Name);
            Assert.All(tool.Required, name => Assert.True(tool.Properties.ContainsKey(name), $"{tool.Name}.{name}"));
        });
    }

    [Fact]
    public void Each_user_message_starts_with_the_date()
    {
        Assert.Equal("<context>Today's date in India: 2026-10-14 (Wednesday).</context>", AssistantPrompt.DateLine(new DateOnly(2026, 10, 14)));
    }
}

public sealed class AssistantToolPermissionTests
{
    private sealed class User(params string[] permissions) : SalesApp.Core.Security.ICurrentUser
    {
        public Guid UserId { get; } = Guid.NewGuid();

        public bool Has(string permission) => permissions.Contains(permission);

        public string RoleName => "Test";
    }

    // Refusal only looks at the user, so the lookups' other dependencies aren't needed here.
    private static AssistantTools Tools(params string[] permissions) => new(null!, null!, null!, new User(permissions));

    [Fact]
    public void Lookups_need_the_same_permission_as_the_screen()
    {
        var reportsOnly = Tools(SalesApp.Core.Security.Permissions.ReportsView);

        Assert.True(reportsOnly.Refusal(AssistantPrompt.ListInvoices)?.IsError);
        Assert.Equal("No access to invoices", reportsOnly.Refusal(AssistantPrompt.ListInvoices)?.Label);
        Assert.NotNull(reportsOnly.Refusal(AssistantPrompt.SearchCustomers));
        Assert.NotNull(reportsOnly.Refusal(AssistantPrompt.QuerySalesData));
        Assert.NotNull(reportsOnly.Refusal(AssistantPrompt.CreateDraft));

        var invoices = Tools(SalesApp.Core.Security.Permissions.InvoicesView);
        Assert.Null(invoices.Refusal(AssistantPrompt.ListInvoices));
        Assert.NotNull(invoices.Refusal(AssistantPrompt.SearchProducts));
    }
}
