using System.Text;

namespace JqSharp.Tests;

// Runs the cases of gojq's cli/test.yaml (converted to testdata/cli-cases.jsonl) through the library,
// emulating the parts of the gojq command they use: input decoding, -n, -s, -c, -r, -j, --indent, --tab and arguments.
public class CliCaseTests
{
    private static readonly Dictionary<string, string> SkippedCases = new()
    {
        ["debug/0 function"] = "debug is defined by the command",
        ["debug/1 function"] = "debug is defined by the command",
        ["stderr function"] = "stderr is defined by the command",
        ["debug and stderr in builtins"] = "debug and stderr are defined by the command",
        ["format strings not defined error with optional operator"] = "debug is defined by the command",
        ["input_filename function with stdin input"] = "input_filename is defined by the command",
        ["input_filename function with slurp option"] = "input_filename is defined by the command",
        ["input_filename function with null input option"] = "input_filename is defined by the command",
        ["input_filename in builtins"] = "input_filename is defined by the command",
        ["search directory in query"] = "needs the module search paths of the command",
        ["module directory option parse error"] = "needs the module search paths of the command",
        ["arg and argjson options"] = "the command resolves duplicate argument names",
        ["argjson option with empty value"] = "command line argument error message",
        ["argjson option with adjacent values"] = "command line argument error message",
        ["argjson option with multiple values"] = "command line argument error message",
        ["argjson option with trailing parse error"] = "command line argument error message",
        ["argjson option parse error"] = "command line argument error message",
        ["jsonargs option with empty value"] = "command line argument error message",
        ["jsonargs option with multiple values"] = "command line argument error message",
        ["jsonargs option parse error"] = "command line argument error message",
        ["invalid json unexpected eof error"] = "the command prints the input position",
        ["invalid json unexpected eof error with multibyte characters"] = "the command prints the input position",
        ["invalid json unexpected eof error with multiple lines"] = "the command prints the input position",
        ["invalid json string literal"] = "the command prints the input position",
        ["invalid json invalid character with multibyte characters"] = "the command prints the input position",
        ["json input error json with multibyte character in the error line"] = "the command prints the input position",
        ["match function regular expression error"] = ".NET regular expression error message differs from Go regexp",
    };

    private static readonly Lazy<Dictionary<string, Dictionary<string, object?>>> Cases = new(LoadCases);

    public static IEnumerable<object[]> CaseNames()
    {
        return Cases.Value.Keys.Where(static name => !SkippedCases.ContainsKey(name)).Select(static name => new object[] { name });
    }

    static CliCaseTests()
    {
        // gojq's test runner pins the local time zone to UTC-7
        GoTime.Local = GoTime.Location.Fixed("UTC-7", -7 * 60 * 60);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Should_MatchGojq_When_CliCaseIsRun(string name)
    {
        Dictionary<string, object?> tc = Cases.Value[name];
        (string stdout, string stderr) = Run(tc);
        string expected = (string)tc["expected"]!;
        string? error = tc["error"] as string;
        Assert.Equal(expected, stdout);
        if (error == null)
        {
            Assert.Equal("", stderr);
        }
        else if (!stderr.StartsWith(error, StringComparison.Ordinal) && !stderr.EndsWith(error, StringComparison.Ordinal))
        {
            Assert.Equal(error, stderr);
        }
    }

    [Fact]
    public void Should_ReferToExistingCases_When_CasesAreSkipped()
    {
        Assert.All(SkippedCases.Keys, name => Assert.Contains(name, Cases.Value.Keys));
    }

    private static Dictionary<string, Dictionary<string, object?>> LoadCases()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "cli-cases.jsonl");
        Dictionary<string, Dictionary<string, object?>> cases = new();
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length > 0)
            {
                Dictionary<string, object?> tc = (Dictionary<string, object?>)JqValue.FromJson(line)!;
                cases[(string)tc["name"]!] = tc;
            }
        }
        return cases;
    }

    private static (string Stdout, string Stderr) Run(Dictionary<string, object?> tc)
    {
        StringBuilder stdout = new();
        StringBuilder stderr = new();
        List<string> names = new();
        List<object?> values = new();
        Dictionary<string, object?> named = new();
        foreach (List<object?> arg in ((List<object?>)tc["named"]!).Cast<List<object?>>())
        {
            object? value = (string)arg[0]! == "json" ? JqValue.FromJson((string)arg[2]!) : arg[2];
            names.Add("$" + (string)arg[1]!);
            values.Add(value);
            named[(string)arg[1]!] = value;
        }
        List<object?> positional = new();
        foreach (List<object?> arg in ((List<object?>)tc["positional"]!).Cast<List<object?>>())
        {
            positional.Add((string)arg[0]! == "json" ? JqValue.FromJson((string)arg[1]!) : arg[1]);
        }
        names.Add("$ARGS");
        values.Add(new Dictionary<string, object?> { ["named"] = named, ["positional"] = positional });

        IEnumerator<object?> inputs = ReadInputs((string?)tc["input"] ?? "", tc["slurp"] is true, stderr).GetEnumerator();
        JqCode code;
        try
        {
            JqQuery query = JqQuery.Parse((string)tc["query"]!);
            code = query.Compile(new JqCompilerOptions
            {
                Variables = names,
                InputIter = new SharedEnumerable(inputs),
                EnvironLoader = static () => new[] { "__JQSHARP_TEST=1" },
            });
        }
        catch (JqParseException err)
        {
            // the command also prints the query with a marker under the error position; compare the message line only
            string expectedError = tc["error"] as string ?? "";
            return ("", expectedError.StartsWith("invalid query: ", StringComparison.Ordinal) && expectedError.EndsWith("  " + err.Message + "\n", StringComparison.Ordinal)
                ? expectedError
                : "invalid query: " + err.Message + "\n");
        }
        catch (JqException err)
        {
            return ("", "compile error: " + err.Message + "\n");
        }

        IEnumerable<object?> runs = tc["null_input"] is true ? new object?[] { null } : new SharedEnumerable(inputs);
        int indent = tc["compact"] is true ? -1 : tc["tab"] is true ? 1 : tc["indent"] is JsonNumber n ? int.Parse(n.Value) : 2;
        bool raw = tc["raw"] is true || tc["join"] is true;
        foreach (object? input in runs)
        {
            try
            {
                foreach (object? v in code.Run(input, values.ToArray()))
                {
                    stdout.Append(raw && v is string s ? s : PrettyEncoder.Encode(v, indent, tc["tab"] is true));
                    if (tc["join"] is not true)
                    {
                        stdout.Append('\n');
                    }
                }
            }
            catch (JqHaltException err)
            {
                if (err.Value != null)
                {
                    stderr.Append(err.Value is string s ? s : JqValue.ToJson(err.Value) + "\n");
                }
                break;
            }
            catch (JqException err)
            {
                stderr.Append(err.Message).Append('\n');
            }
        }
        return (stdout.ToString(), stderr.ToString());
    }

    private static IEnumerable<object?> ReadInputs(string input, bool slurp, StringBuilder stderr)
    {
        List<object?> values = new();
        int pos = 0;
        while (true)
        {
            object? v;
            try
            {
                if (!Decoder.TryDecode(input, ref pos, out v))
                {
                    break;
                }
            }
            catch (JqException err)
            {
                stderr.Append("invalid json: <stdin>: ").Append(err.Message).Append('\n');
                break;
            }
            if (!slurp)
            {
                yield return v;
            }
            values.Add(v);
        }
        if (slurp)
        {
            yield return values;
        }
    }

    // Lets the query input loop and input/inputs consume the same input sequence.
    private sealed class SharedEnumerable(IEnumerator<object?> enumerator) : IEnumerable<object?>
    {
        public IEnumerator<object?> GetEnumerator() => new NonDisposing(enumerator);

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class NonDisposing(IEnumerator<object?> inner) : IEnumerator<object?>
        {
            public object? Current => inner.Current;

            object? System.Collections.IEnumerator.Current => Current;

            public bool MoveNext() => inner.MoveNext();

            public void Reset() => inner.Reset();

            public void Dispose()
            {
            }
        }
    }
}
