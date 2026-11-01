namespace JqSharp;

/// <summary>
/// Options for <see cref="JqQuery.Compile"/>.
/// </summary>
public sealed class JqCompilerOptions
{
    /// <summary>
    /// Names of the variables the query can use, each starting with <c>$</c>. Their values are passed to
    /// <see cref="JqCode.Run"/> in the same order.
    /// </summary>
    public IReadOnlyList<string>? Variables { get; set; }

    /// <summary>
    /// Returns the environment as <c>NAME=value</c> strings for <c>$ENV</c> and <c>env</c>; it is called at compile time.
    /// The process environment is not visible by default.
    /// </summary>
    public Func<IEnumerable<string>>? EnvironLoader { get; set; }

    /// <summary>
    /// The values <c>input</c> and <c>inputs</c> read. They are not allowed without it. The sequence is enumerated
    /// once, lazily, and shared by every run of the compiled code.
    /// </summary>
    public IEnumerable<object?>? InputIter { get; set; }

    /// <summary>
    /// Loads the modules of <c>import</c> and <c>include</c> directives; see <see cref="JqModuleLoader"/> for loading them from files.
    /// </summary>
    public IJqModuleLoader? ModuleLoader { get; set; }
}
