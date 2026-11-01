namespace JqSharp;

internal enum Opcode
{
    Nop,
    Push,
    Pop,
    Dup,
    Const,
    Load,
    Store,
    Object,
    Append,
    Fork,
    ForkTryBegin,
    ForkTryEnd,
    ForkAlt,
    ForkLabel,
    Backtrack,
    Jump,
    JumpIfNot,
    Index,
    IndexArray,
    Call,
    CallRec,
    PushPc,
    CallPc,
    Scope,
    Ret,
    Iter,
    ExpBegin,
    ExpEnd,
    PathBegin,
    PathEnd,
}

// One instruction. V holds the operand: a constant value, a jump target (int), a variable (VarRef),
// a scope (ScopeRef), or the function to call (int pc or CallFunc).
internal sealed class Code(Opcode op, object? v = null)
{
    public Opcode Op = op;
    public object? V = v;

    public override string ToString() => Op + " " + V;
}

// A variable slot: the scope id and the index in the scope.
internal readonly struct VarRef(int scope, int index) : IEquatable<VarRef>
{
    public readonly int Scope = scope;
    public readonly int Index = index;

    public bool Equals(VarRef other) => Scope == other.Scope && Index == other.Index;

    public override bool Equals(object? obj) => obj is VarRef v && Equals(v);

    public override int GetHashCode() => (Scope * 397) ^ Index;
}

// The operand of a scope instruction: scope id, number of variables and number of arguments.
internal readonly struct ScopeRef(int id, int variables, int args)
{
    public readonly int Id = id;
    public readonly int Variables = variables;
    public readonly int Args = args;
}

// A call of a native function: the callback, its argument count and its name.
internal sealed class CallFunc(Func<object?, object?[], object?> callback, int argCount, string name)
{
    public Func<object?, object?[], object?> Callback { get; } = callback;

    public int ArgCount { get; } = argCount;

    public string Name { get; } = name;
}

// A closure passed as a function argument: its pc and the scope it was created in.
internal readonly struct Closure(int pc, int scopeIndex)
{
    public readonly int Pc = pc;
    public readonly int ScopeIndex = scopeIndex;
}

/// <summary>
/// A compiled jq query, created by <see cref="JqQuery.Compile"/>.
/// </summary>
/// <remarks>
/// Running a compiled query is thread-safe, so one instance can be reused for many inputs.
/// </remarks>
public sealed class JqCode
{
    private readonly IReadOnlyList<string> _variables;
    private readonly Code[] _codes;

    internal JqCode(IReadOnlyList<string> variables, Code[] codes)
    {
        _variables = variables;
        _codes = codes;
    }

    internal Code[] Codes => _codes;

    /// <summary>
    /// Runs the query against an input value.
    /// </summary>
    /// <param name="input">
    /// The input value: <see langword="null"/>, <see cref="bool"/>, a number (<see cref="long"/>, <see cref="double"/>,
    /// <see cref="System.Numerics.BigInteger"/>, <see cref="JsonNumber"/>, or another primitive numeric type, which is converted),
    /// <see cref="string"/>, <c>List&lt;object?&gt;</c> for arrays, or <c>Dictionary&lt;string, object?&gt;</c> for objects.
    /// </param>
    /// <param name="values">The values of the variables declared in <see cref="JqCompilerOptions.Variables"/>, in the same order.</param>
    /// <returns>
    /// The output values, produced lazily. Enumeration throws a <see cref="JqException"/> when the query fails;
    /// <see cref="JqHaltException"/> means the query called <c>halt</c> or <c>halt_error</c>.
    /// </returns>
    public IEnumerable<object?> Run(object? input, params object?[] values)
    {
        values ??= Array.Empty<object?>();
        if (values.Length > _variables.Count)
        {
            return Fail(new TooManyVariableValuesError());
        }
        if (values.Length < _variables.Count)
        {
            return Fail(new ExpectedVariableError(_variables[values.Length]));
        }
        object?[] vars = new object?[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            vars[i] = JqValue.Normalize(values[i]);
        }
        return Iterate(new Env(_codes, JqValue.Normalize(input), vars));
    }

    private static IEnumerable<object?> Fail(JqException err)
    {
        throw err;
#pragma warning disable CS0162 // makes this an iterator, so the error surfaces on enumeration like gojq's error iterator
        yield break;
#pragma warning restore CS0162
    }

    private static IEnumerable<object?> Iterate(Env env)
    {
        while (env.Next(out object? v))
        {
            if (v is JqException err)
            {
                throw err;
            }
            yield return v;
        }
    }
}
