namespace JqSharp.Tests;

// Regressions found in dogfooding round 1 (docs/dogfooding/round1.md).
public class DogfoodRound1Tests
{
    private static string RunJson(string src, string input)
    {
        return string.Join("\n", JqQuery.Parse(src).Run(JqValue.FromJson(input)).Select(JqValue.ToJson));
    }

    private static object? Nest(int depth)
    {
        object? v = 1L;
        for (int i = 0; i < depth; i++)
        {
            v = new List<object?> { v };
        }
        return v;
    }

    // The depth guard trips relative to the stack size; pin 1 MB so Linux (8 MB default) behaves like Windows.
    private static void AssertThrowsOnSmallStack(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { caught = e; }
        }, 1024 * 1024);
        thread.Start();
        thread.Join();

        Assert.IsAssignableFrom<JqException>(caught);
    }

    [Fact]
    public void Should_RestartQuery_When_ResultsAreEnumeratedTwice()
    {
        IEnumerable<object?> results = JqQuery.Parse(".[]").Compile().Run(new List<object?> { 1L, 2L, 3L });

        Assert.True(results.Any());
        Assert.Equal(new object?[] { 1L, 2L, 3L }, results.ToList());
    }

    [Fact]
    public void Should_DecodeNestedJson_When_DepthIsModerate()
    {
        string json = new string('[', 1000) + new string(']', 1000);

        Assert.Equal(json, JqValue.ToJson(JqValue.FromJson(json)));
    }

    [Fact]
    public void Should_ThrowJqException_When_JsonIsTooDeep()
    {
        string json = new string('[', 100000) + new string(']', 100000);

        JqException err = Assert.ThrowsAny<JqException>(() => JqValue.FromJson(json));
        Assert.Contains("exceeded max depth", err.Message);
        Assert.Equal("true", RunJson("try fromjson catch contains(\"exceeded max depth\")", JqValue.ToJson(json)));
    }

    [Fact]
    public void Should_ThrowJqException_When_ValueIsTooDeep()
    {
        object? deep = Nest(100000);

        AssertThrowsOnSmallStack(() => JqValue.ToJson(deep));
        AssertThrowsOnSmallStack(() => JqQuery.Parse(".").Run(deep).ToList());
    }

    [Theory]
    [InlineData("reduce range(100000) as $i (0; [.]) | tojson")]
    [InlineData("reduce range(100000) as $i (0; [.]) | . == .")]
    [InlineData("reduce range(100000) as $i (0; [.]) | [., .] | sort | length")]
    [InlineData("reduce range(100000) as $i (0; [.]) | flatten | length")]
    [InlineData("reduce range(100000) as $i (0; {a: .}) | . * . | length")]
    [InlineData("reduce range(100000) as $i (0; [.]) | contains(.)")]
    [InlineData("null | setpath([range(100000) | 0]; 1) | length")]
    public void Should_ThrowJqException_When_QueryBuildsTooDeepValue(string src)
    {
        AssertThrowsOnSmallStack(() => JqQuery.Parse(src).Run(null).ToList());
    }

    [Theory]
    [InlineData(100000, "[", "]")]
    [InlineData(100000, ". + 1 | ", ".")]
    [InlineData(100000, "(", ")")]
    [InlineData(100000, "if . then ", " else 1 end")]
    public void Should_ThrowJqException_When_QueryIsTooDeep(int depth, string open, string close)
    {
        string src = string.Concat(Enumerable.Repeat(open, depth)) + string.Concat(Enumerable.Repeat(close, close == "." ? 1 : depth));

        AssertThrowsOnSmallStack(() => JqQuery.Parse(src).Compile());
    }

    [Fact]
    public void Should_PrintQuery_When_QueryIsModeratelyDeep()
    {
        string src = new string('[', 500) + new string(']', 500);

        Assert.Equal(src, JqQuery.Parse(src).ToString());
    }

    [Fact]
    public void Should_RejectUnsupportedInputTypes_When_RunIsCalled()
    {
        JqCode code = JqQuery.Parse("length").Compile();

        ArgumentException err = Assert.Throws<ArgumentException>(() => code.Run(new[] { 1, 2 }));
        Assert.Contains("System.Int32[]", err.Message);
        Assert.Throws<ArgumentException>(() => code.Run(new List<object?> { new List<int> { 1 } }));
        Assert.Throws<ArgumentException>(() => JqQuery.Parse("$x").Compile(new JqCompilerOptions { Variables = new[] { "$x" } }).Run(null, new DateTime(1970, 1, 1)));
        Assert.Throws<ArgumentException>(() => JqValue.ToJson(new Dictionary<string, object?> { ["a"] = 'c' }));
    }

    [Theory]
    [InlineData("[scan(\".\")]", "\"\\ud83d\\ude00a\"", "[\"\\ud83d\\ude00\",\"a\"]")]
    [InlineData("gsub(\"\"; \"-\")", "\"\\ud83d\\ude00\"", "\"-\\ud83d\\ude00-\"")]
    [InlineData("[splits(\"\")]", "\"a\\ud83d\\ude00\"", "[\"\",\"a\",\"\\ud83d\\ude00\",\"\"]")]
    [InlineData("[match(\"\"; \"g\") | .offset]", "\"\\u00e9\\ud83d\\ude00\"", "[0,1,2]")]
    [InlineData("[match(\".\"; \"g\") | [.offset, .length]]", "\"\\ud83d\\ude00\\u00e9\"", "[[0,1],[1,1]]")]
    [InlineData("test(\"^.$\")", "\"\\ud83d\\ude00\"", "true")]
    [InlineData("capture(\"(?<a>.)\")", "\"\\ud83d\\ude00\"", "{\"a\":\"\\ud83d\\ude00\"}")]
    [InlineData("[match(\"[.]\"; \"g\") | .offset]", "\"a.b\"", "[1]")]
    [InlineData("[match(\"\\\\.\"; \"g\") | .offset]", "\"a.b\"", "[1]")]
    [InlineData("test(\"a.b\")", "\"a\\nb\"", "false")]
    [InlineData("test(\"a.b\"; \"m\")", "\"a\\nb\"", "true")]
    public void Should_MatchCodePoints_When_StringHasSurrogatePairs(string src, string input, string expected)
    {
        // Expected outputs are gojq's; RE2 matches code points, .NET matches UTF-16 code units.
        Assert.Equal(JqValue.ToJson(JqValue.FromJson(expected)), RunJson(src, input));
    }

    [Fact]
    public void Should_NotLoadAbsoluteModulePaths_When_UsingJqModuleLoader()
    {
        string root = Path.Combine(Path.GetTempPath(), "jqsharp-modules-" + Guid.NewGuid().ToString("N"));
        string search = Path.Combine(root, "search");
        Directory.CreateDirectory(search);
        File.WriteAllText(Path.Combine(root, "outside.jq"), "def f: 1;");
        File.WriteAllText(Path.Combine(search, "inside.jq"), "def f: 2;");
        try
        {
            JqCompilerOptions options = new() { ModuleLoader = new JqModuleLoader(new[] { search }) };
            string outside = Path.Combine(root, "outside").Replace('\\', '/');

            Assert.Equal(new object?[] { 2L }, JqQuery.Parse("import \"inside\" as m; m::f").Compile(options).Run(null).ToList());
            Assert.Equal(new object?[] { 1L }, JqQuery.Parse("import \"../outside\" as m; m::f").Compile(options).Run(null).ToList());
            JqException err = Assert.ThrowsAny<JqException>(() => JqQuery.Parse($"import \"{outside}\" as m; m::f").Compile(options));
            Assert.StartsWith("module not found", err.Message);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Should_ThrowArgumentNullException_When_ModuleLoaderPathsAreNull()
    {
        Assert.Throws<ArgumentNullException>(() => new JqModuleLoader(null!));
    }

    // Recommendations resolved after round 1.

    [Theory]
    [InlineData("def f: 1 + f; f")]
    [InlineData("[range(1e9)]")]
    [InlineData("last(repeat(1))")]
    public void Should_StopRunawayQuery_When_TokenIsCanceled(string src)
    {
        JqCode code = JqQuery.Parse(src).Compile();
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(100));
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

        Assert.ThrowsAny<OperationCanceledException>(() => code.Run(null, cts.Token).ToList());
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), sw.Elapsed.ToString());
    }

    [Fact]
    public void Should_GiveSameResults_When_TokenIsNotCanceled()
    {
        JqCompilerOptions options = new() { Variables = new[] { "$x" } };
        JqCode code = JqQuery.Parse(".[] + $x, (try error(\"e\") catch .)").Compile(options);
        List<object?> input = new() { 1L, 2L };
        using CancellationTokenSource cts = new();

        List<object?> expected = code.Run(input, 10L).ToList();
        Assert.Equal(new object?[] { 11L, 12L, "e" }, expected);
        Assert.Equal(expected, code.Run(input, cts.Token, 10L).ToList());
        Assert.Equal(new object?[] { 1L, 2L }, JqQuery.Parse(".[]").Run(input, cts.Token).ToList());
    }

    [Fact]
    public void Should_Throw_When_TokenIsAlreadyCanceled()
    {
        IEnumerable<object?> results = JqQuery.Parse(".").Compile().Run(1L, new CancellationToken(true));

        Assert.ThrowsAny<OperationCanceledException>(() => results.ToList());
    }

    [Theory]
    [InlineData("1 2", "invalid JSON at offset 2: invalid character '2' after top-level value")]
    [InlineData("01", "invalid JSON at offset 1: invalid character '1' after top-level value")]
    [InlineData("{} {}", "invalid JSON at offset 3: invalid character '{' after top-level value")]
    [InlineData("[1, tru!]", "invalid JSON at offset 7: invalid character '!' in literal true (expecting 'e')")]
    [InlineData("[1,", "invalid JSON at offset 3: unexpected EOF")]
    [InlineData(" ", "invalid JSON at offset 1: unexpected EOF")]
    public void Should_ReportReasonAndOffset_When_FromJsonFails(string json, string message)
    {
        JqException err = Assert.ThrowsAny<JqException>(() => JqValue.FromJson(json));

        Assert.Equal(message, err.Message);
        Assert.Equal(JqValue.ToJson("fromjson cannot be applied to: string (\"1 2\")"), RunJson("try fromjson catch .", "\"1 2\""));
    }

    private sealed class NullModuleLoader : IJqModuleLoader
    {
        public IEnumerable<JqQuery> LoadInitModules() => Array.Empty<JqQuery>();

        public JqQuery LoadModule(string name, Dictionary<string, object?>? meta) => null!;

        public object? LoadJson(string name, Dictionary<string, object?>? meta) => null;
    }

    [Fact]
    public void Should_ReportModuleNotFound_When_CustomLoaderReturnsNull()
    {
        JqCompilerOptions options = new() { ModuleLoader = new NullModuleLoader() };

        JqException err = Assert.ThrowsAny<JqException>(() => JqQuery.Parse("import \"m\" as m; m::f").Compile(options));
        Assert.Equal("module not found: \"m\"", err.Message);
        Assert.Equal(JqValue.ToJson("module not found: \"m\""),
            string.Join("", JqQuery.Parse("\"m\" | try modulemeta catch .").Compile(options).Run(null).Select(JqValue.ToJson)));
    }
}
