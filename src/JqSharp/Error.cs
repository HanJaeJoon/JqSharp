namespace JqSharp;

/// <summary>
/// The base class of every error JqSharp reports: parse errors, compile errors and runtime errors.
/// </summary>
/// <remarks>
/// The messages are the same as gojq's.
/// </remarks>
public class JqException : Exception
{
    private readonly string? _message;

    internal JqException()
    {
    }

    internal JqException(string message)
    {
        _message = message;
    }

    /// <inheritdoc/>
    public override string Message => _message ?? base.Message;
}

/// <summary>
/// A syntax error in a query.
/// </summary>
public sealed class JqParseException : JqException
{
    internal JqParseException(int offset, string token, string message)
        : base(message)
    {
        Offset = offset;
        Token = token;
    }

    /// <summary>
    /// The number of UTF-8 bytes of the query read when the error occurred.
    /// </summary>
    public int Offset { get; }

    /// <summary>
    /// The token that caused the error; empty when the error occurred after reading the whole query.
    /// </summary>
    public string Token { get; }
}

/// <summary>
/// An error raised with a value, like the <c>error</c> builtin; <c>try ... catch</c> catches the value itself.
/// </summary>
public class JqValueException : JqException
{
    internal JqValueException(object? value, int exitCode)
    {
        Value = value;
        ExitCode = exitCode;
    }

    /// <summary>
    /// The error value.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// The exit code the gojq command would use.
    /// </summary>
    public int ExitCode { get; }

    /// <inheritdoc/>
    public override string Message => Value is string s ? "error: " + s : "error: " + Encoder.Marshal(Value);
}

/// <summary>
/// Raised by the <c>halt</c> and <c>halt_error</c> builtins to stop the program; it is not catchable by <c>try</c>.
/// </summary>
/// <remarks>
/// <c>halt</c> raises it with a <see langword="null"/> value, which means "stop without an error".
/// </remarks>
public sealed class JqHaltException : JqValueException
{
    internal JqHaltException(object? value, int exitCode)
        : base(value, exitCode)
    {
    }

    /// <inheritdoc/>
    public override string Message => "halt " + base.Message;
}

internal sealed class ExpectedObjectError(object? v) : JqException
{
    public override string Message => "expected an object but got: " + Jq.TypeErrorPreview(v);
}

internal sealed class ExpectedArrayError(object? v) : JqException
{
    public override string Message => "expected an array but got: " + Jq.TypeErrorPreview(v);
}

internal sealed class IteratorError(object? v) : JqException
{
    public override string Message => "cannot iterate over: " + Jq.TypeErrorPreview(v);
}

internal sealed class ArrayIndexNegativeError(long v) : JqException
{
    public override string Message => "array index should not be negative: " + Jq.Preview(v);
}

internal sealed class ArrayIndexTooLargeError(object? v) : JqException
{
    public override string Message => "array index too large: " + Jq.Preview(v);
}

internal sealed class RepeatStringTooLargeError(string s, double n) : JqException
{
    public override string Message => "repeat string result too large: " + Jq.Preview(s) + " * " + Jq.Preview(n);
}

internal sealed class ObjectKeyNotStringError(object? v) : JqException
{
    public override string Message => "expected a string for object key but got: " + Jq.TypeErrorPreview(v);
}

internal sealed class ArrayIndexNotNumberError(object? v) : JqException
{
    public override string Message => "expected a number for indexing an array but got: " + Jq.TypeErrorPreview(v);
}

internal sealed class StringIndexNotNumberError(object? v) : JqException
{
    public override string Message => "expected a number for indexing a string but got: " + Jq.TypeErrorPreview(v);
}

internal sealed class ExpectedStartEndError(object? v) : JqException
{
    public override string Message => "expected \"start\" and \"end\" for slicing but got: " + Jq.TypeErrorPreview(v);
}

internal sealed class LengthMismatchError() : JqException("length mismatch");

internal sealed class InputNotAllowedError() : JqException("input(s)/0 is not allowed");

internal sealed class FuncNotFoundError(FuncCall f) : JqException("function not defined: " + f.Name + "/" + f.ArgCount)
{
    public FuncCall Func => f;
}

internal sealed class Func0TypeError(string name, object? v) : JqException
{
    public override string Message => name + " cannot be applied to: " + Jq.TypeErrorPreview(v);
}

internal sealed class Func1TypeError(string name, object? v, object? w) : JqException
{
    public override string Message => name + "(" + Jq.Preview(w) + ") cannot be applied to: " + Jq.TypeErrorPreview(v);
}

internal sealed class Func2TypeError(string name, object? v, object? w, object? x) : JqException
{
    public override string Message => name + "(" + Jq.Preview(w) + "; " + Jq.Preview(x) + ") cannot be applied to: " + Jq.TypeErrorPreview(v);
}

internal sealed class Func0WrapError(string name, object? v, Exception err) : JqException
{
    public override string Message => name + " cannot be applied to " + Jq.Preview(v) + ": " + err.Message;
}

internal sealed class Func1WrapError(string name, object? v, object? w, Exception err) : JqException
{
    public override string Message => name + "(" + Jq.Preview(w) + ") cannot be applied to " + Jq.Preview(v) + ": " + err.Message;
}

internal sealed class Func2WrapError(string name, object? v, object? w, object? x, Exception err) : JqException
{
    public override string Message => name + "(" + Jq.Preview(w) + "; " + Jq.Preview(x) + ") cannot be applied to " + Jq.Preview(v) + ": " + err.Message;
}

internal sealed class FlattenDepthError(double v) : JqException
{
    public override string Message => "flatten depth should not be negative: " + Jq.Preview(v);
}

internal sealed class TimeArrayError() : JqException("expected an array of 8 numbers");

internal sealed class TimeEpochError() : JqException("number of seconds is out of range");

internal sealed class TimeNumberError() : JqException("number is out of range");

internal sealed class UnaryTypeError(string name, object? v) : JqException
{
    public override string Message => "cannot " + name + ": " + Jq.TypeErrorPreview(v);
}

internal sealed class BinopTypeError(string name, object? l, object? r) : JqException
{
    public override string Message => "cannot " + name + ": " + Jq.TypeErrorPreview(l) + " and " + Jq.TypeErrorPreview(r);
}

internal sealed class ZeroDivisionError(object? l, object? r) : JqException
{
    public override string Message => "cannot divide " + Jq.TypeErrorPreview(l) + " by: " + Jq.TypeErrorPreview(r);
}

internal sealed class ZeroModuloError(object? l, object? r) : JqException
{
    public override string Message => "cannot modulo " + Jq.TypeErrorPreview(l) + " by: " + Jq.TypeErrorPreview(r);
}

internal sealed class FormatNotFoundError(string n) : JqException("format not defined: " + n);

internal sealed class FormatRowError(string typ, object? v) : JqException
{
    public override string Message => "@" + typ + " cannot format an array including: " + Jq.TypeErrorPreview(v);
}

internal sealed class TooManyVariableValuesError() : JqException("too many variable values provided");

internal sealed class ExpectedVariableError(string n) : JqException("variable defined but not bound: " + n);

internal sealed class VariableNotFoundError(string n) : JqException("variable not defined: " + n);

internal sealed class VariableNameError(string n) : JqException("invalid variable name: " + n);

internal sealed class BreakError(string n, object? v) : JqException("label not defined: " + n)
{
    public object? Value => v;
}

internal sealed class TryEndError(JqException err) : JqException
{
    public JqException Err => err;

    public override string Message => err.Message;
}

internal sealed class InvalidPathError(object? v) : JqException
{
    public override string Message => "invalid path against: " + Jq.TypeErrorPreview(v);
}

internal sealed class InvalidPathIterError(object? v) : JqException
{
    public override string Message => "invalid path on iterating against: " + Jq.TypeErrorPreview(v);
}

internal sealed class QueryParseError(string fname, JqException err) : JqException("invalid query: " + fname + ": " + err.Message);

internal sealed class JsonParseError(string fname, Exception err) : JqException("invalid json: " + fname + ": " + err.Message);

// Plain errors gojq creates with errors.New or fmt.Errorf.
internal sealed class SimpleError(string message) : JqException(message);
