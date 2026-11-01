namespace JqSharp.Tests;

// Ported from gojq query_test.go. gojq keeps iterating after an error; JqSharp throws the first one.
public class QueryTests
{
    private static List<object?> Run(string src, object? input)
    {
        return JqQuery.Parse(src).Run(input).ToList();
    }

    [Fact]
    public void Should_IterateRecursively_When_QueryRuns()
    {
        Dictionary<string, object?> input = new() { ["foo"] = new List<object?> { 1, 2, 3 } };

        List<object?> got = Run(".foo | ..", input);

        Assert.Equal(new object?[] { new List<object?> { 1L, 2L, 3L }, 1L, 2L, 3L }, got);
    }

    [Fact]
    public void Should_RunConcurrently_When_QueryIsShared()
    {
        JqQuery query = JqQuery.Parse(".foo");
        Dictionary<string, object?> v = new() { ["foo"] = 1L };

        Parallel.For(0, 3, _ => Assert.Equal(new object?[] { 1L }, query.Run(v).ToList()));
    }

    [Fact]
    public void Should_ThrowFirstError_When_ErrorIsRaised()
    {
        JqValueException err = Assert.Throws<JqValueException>(() => Run(".[] | error", new List<object?> { 0, 1, 2, 3, 4 }));

        Assert.Equal("error: 0", err.Message);
        Assert.Equal(0L, err.Value);
    }

    [Fact]
    public void Should_StopWithHaltError_When_HaltIsCalled()
    {
        List<object?> got = new();
        JqHaltException err = Assert.Throws<JqHaltException>(() =>
        {
            foreach (object? v in JqQuery.Parse("0, halt, 1").Run(null))
            {
                got.Add(v);
            }
        });

        Assert.Equal(new object?[] { 0L }, got);
        Assert.Null(err.Value);
    }

    [Fact]
    public void Should_ReportValue_When_HaltErrorIsCalled()
    {
        JqHaltException err = Assert.Throws<JqHaltException>(() => Run(".[] | halt_error", new List<object?> { "foo", "bar", "baz" }));

        Assert.Equal("halt error: foo", err.Message);
        Assert.Equal(5, err.ExitCode);
    }

    [Fact]
    public void Should_ReportObjectKeyError_When_KeyIsNotString()
    {
        JqException err = Assert.ThrowsAny<JqException>(() => Run(".[] | {(.): 1}", new List<object?> { 0, "x", new List<object?>() }));

        Assert.Contains("expected a string for object key but got", err.Message);
        Assert.Equal(new object?[] { new Dictionary<string, object?> { ["x"] = 1L } }, Run(".[] | {(.): 1}", new List<object?> { "x" }));
    }

    [Fact]
    public void Should_ReportIndexError_When_IndexingArrayWithKey()
    {
        JqException err = Assert.ThrowsAny<JqException>(() => Run(".foo", new List<object?> { 0 }));

        Assert.Equal("expected an object but got: array ([0])", err.Message);
    }

    [Fact]
    public void Should_ReportInvalidPath_When_PathIsNotTracked()
    {
        List<object?> got = new();
        JqException err = Assert.ThrowsAny<JqException>(() =>
        {
            foreach (object? v in JqQuery.Parse(". + 1, path(. + 1)").Run(0))
            {
                got.Add(v);
            }
        });

        Assert.Equal(new object?[] { 1L }, got);
        Assert.Equal("invalid path against: number (1)", err.Message);
    }

    [Theory]
    [InlineData("path([][])", "invalid path on iterating against: array ([])")]
    [InlineData("path([1][])", "invalid path on iterating against: array ([1])")]
    [InlineData("path({}[])", "invalid path on iterating against: object ({})")]
    [InlineData("path({\"a\":1}[])", "invalid path on iterating against: object ({\"a\":1})")]
    public void Should_ReportInvalidPathIter_When_IteratingConstant(string src, string expected)
    {
        JqException err = Assert.ThrowsAny<JqException>(() => Run(src, 0));

        Assert.Equal(expected, err.Message);
    }

    [Fact]
    public void Should_ReportIteratorError_When_IteratingNull()
    {
        JqException err = Assert.ThrowsAny<JqException>(() => Run(".[]", null));

        Assert.Equal("cannot iterate over: null", err.Message);
    }

    [Fact]
    public void Should_DecodeStringLiterals_When_QueryHasEscapes()
    {
        string src = "[\"\0\\\\\", \"\x1f\\\"\", \"\n\\n\n\\(\"\\n\")\n\\n\", \"\\/\", \"\x7f\", \"\\ud83d\\ude04\" | explode[]]";

        List<object?> got = Run(src, null);

        Assert.Equal(new object?[]
        {
            new List<object?> { 0x00L, (long)'\\', 0x1fL, (long)'"', 10L, 10L, 10L, 10L, 10L, 10L, (long)'/', 0x7fL, 128516L },
        }, got);
    }

    [Fact]
    public void Should_RejectInput_When_InputIterIsNotGiven()
    {
        JqException err = Assert.Throws<InputNotAllowedError>(() => Run("input", null));

        Assert.Equal("input(s)/0 is not allowed", err.Message);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("add(.[])")]
    public void Should_SkipNull_When_AddingObjects(string src)
    {
        List<object?> input = new() { null, new Dictionary<string, object?> { ["x"] = 1 } };

        Assert.Equal(new object?[] { new Dictionary<string, object?> { ["x"] = 1L } }, Run(src, input));
    }

    [Theory]
    [InlineData("keys")]
    [InlineData("map(.)")]
    [InlineData("to_entries")]
    [InlineData("arrays")]
    [InlineData("reverse")]
    [InlineData("flatten")]
    [InlineData("sort")]
    [InlineData("sort_by(.)")]
    [InlineData("group_by(.)")]
    [InlineData("unique")]
    [InlineData("unique_by(.)")]
    [InlineData("transpose")]
    [InlineData("nth(.)")]
    [InlineData("indices([])")]
    [InlineData("path(.)")]
    public void Should_ReturnEmptyArray_When_InputIsEmptyArray(string f)
    {
        foreach (object? v in Run("[] | " + f, null))
        {
            Assert.Equal(new List<object?>(), v);
        }
    }

    [Fact]
    public void Should_EmitRange_When_RunConcurrently()
    {
        JqQuery query = JqQuery.Parse("range(10)");

        Parallel.For(0, 10, _ => Assert.Equal(Enumerable.Range(0, 10).Select(static i => (object?)(long)i), query.Run(null).ToList()));
    }

    [Fact]
    public void Should_RoundTrip_When_QueryIsPrinted()
    {
        string builtin;
        using (Stream stream = typeof(JqQuery).Assembly.GetManifestResourceStream("JqSharp.builtin.jq")!)
        using (StreamReader reader = new(stream))
        {
            builtin = reader.ReadToEnd();
        }
        string[] sources =
        {
            builtin,
            "module { a: null, \"b\": [true, false, 1.5, \"c\"], c: {} };\nimport \"a\" as a { search: \"./\" };\nimport \"b\" as $b;\ninclude \"c\" { d: [] };\n.",
            ". as [$a, {$b, c: $c, \"d\": $d, (.e): $e, $f: [$g]}] ?// $h | $a",
            ". as {\"a\\(.b)c\": $d} | $d",
            "\"a\\(.b)c\\(\"d\\(.e)f\")g\", @base64 \"a\\(.b)c\", @json",
            ".a.\"b\".[\"c\"].\"d\\(.e)f\", . .a, 0 .a, ..[]",
            "{a: 1, \"b\": 2, \"c\\(.d)e\": 3, (.f): 4, $g, h}",
        };
        foreach (string src in sources)
        {
            string printed = JqQuery.Parse(src).ToString();

            Assert.Equal(printed, JqQuery.Parse(printed).ToString());
        }
    }
}
