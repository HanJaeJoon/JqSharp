using System.Text;

namespace JqSharp;

/// <summary>
/// The abstract syntax tree of a jq query, as returned by <see cref="Parse"/>.
/// </summary>
/// <remarks>
/// A parsed query is immutable in practice and can be compiled any number of times, from any thread.
/// <see cref="ToString"/> prints the query back in a normalized form.
/// </remarks>
public sealed class JqQuery
{
    internal ConstObject? Meta;
    internal List<Import>? Imports;
    internal List<FuncDef>? FuncDefs;
    internal Term? Term;
    internal JqQuery? Left;
    internal JqQuery? Right;
    internal List<Pattern>? Patterns;
    internal Operator Op;

    internal JqQuery()
    {
    }

    /// <summary>
    /// Parses a query string.
    /// </summary>
    /// <param name="src">The jq query.</param>
    /// <returns>The parsed query.</returns>
    /// <exception cref="JqParseException">The query has a syntax error.</exception>
    public static JqQuery Parse(string src)
    {
        if (src is null)
        {
            throw new ArgumentNullException(nameof(src));
        }
        Lexer l = new(src);
        if (Parser.Parse(l) > 0)
        {
            throw l.Err!;
        }
        return l.Result!;
    }

    /// <summary>
    /// Compiles the query.
    /// </summary>
    /// <param name="options">Compiler options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The compiled code, which can be run any number of times, from any thread.</returns>
    /// <exception cref="JqException">The query refers to an undefined function or variable, or a module fails to load.</exception>
    public JqCode Compile(JqCompilerOptions? options = null)
    {
        return Compiler.Compile(this, options);
    }

    /// <summary>
    /// Compiles and runs the query against one input value.
    /// </summary>
    /// <param name="input">The input value.</param>
    /// <returns>The output values, produced lazily.</returns>
    public IEnumerable<object?> Run(object? input)
    {
        return Compile().Run(input);
    }

    /// <summary>
    /// Compiles and runs the query against one input value, stopping when <paramref name="cancellationToken"/> is canceled.
    /// </summary>
    /// <param name="input">The input value.</param>
    /// <param name="cancellationToken">The token checked while the query runs; see <see cref="JqCode.Run(object?, CancellationToken, object?[])"/>.</param>
    /// <returns>The output values, produced lazily.</returns>
    public IEnumerable<object?> Run(object? input, CancellationToken cancellationToken)
    {
        return Compile().Run(input, cancellationToken);
    }

    /// <summary>
    /// Returns the query in jq syntax.
    /// </summary>
    public override string ToString()
    {
        StringBuilder s = new();
        WriteTo(s);
        return s.ToString();
    }

    internal void WriteTo(StringBuilder s)
    {
        Jq.EnsureStack();
        if (Meta != null)
        {
            s.Append("module ");
            Meta.WriteTo(s);
            s.Append(";\n");
        }
        if (Imports != null)
        {
            foreach (Import im in Imports)
            {
                im.WriteTo(s);
            }
        }
        if (FuncDefs != null)
        {
            foreach (FuncDef fd in FuncDefs)
            {
                fd.WriteTo(s);
                s.Append(' ');
            }
        }
        if (Term != null)
        {
            Term.WriteTo(s);
        }
        else if (Right != null)
        {
            Left!.WriteTo(s);
            Pattern.WritePatternsTo(s, Patterns);
            if (Op != Operator.Comma)
            {
                s.Append(' ');
            }
            s.Append(Op.ToToken());
            s.Append(' ');
            Right.WriteTo(s);
        }
    }

    internal object? ToIndexKey()
    {
        return Term?.ToIndexKey();
    }

    internal List<object?>? ToIndices(List<object?>? xs)
    {
        return Term?.ToIndices(xs);
    }
}

internal sealed class Import
{
    public string ImportPath = "";
    public string ImportAlias = "";
    public string IncludePath = "";
    public ConstObject? Meta;

    public override string ToString()
    {
        StringBuilder s = new();
        WriteTo(s);
        return s.ToString();
    }

    public void WriteTo(StringBuilder s)
    {
        if (ImportPath != "")
        {
            s.Append("import ");
            Encoder.EncodeString(s, ImportPath);
            s.Append(" as ");
            s.Append(ImportAlias);
        }
        else
        {
            s.Append("include ");
            Encoder.EncodeString(s, IncludePath);
        }
        if (Meta != null)
        {
            s.Append(' ');
            Meta.WriteTo(s);
        }
        s.Append(";\n");
    }
}

internal sealed class FuncDef
{
    public string Name = "";
    public List<string>? Args;
    public JqQuery Body = null!;

    public int ArgCount => Args?.Count ?? 0;

    public override string ToString()
    {
        StringBuilder s = new();
        WriteTo(s);
        return s.ToString();
    }

    public void WriteTo(StringBuilder s)
    {
        s.Append("def ");
        s.Append(Name);
        if (Args != null && Args.Count > 0)
        {
            s.Append('(');
            for (int i = 0; i < Args.Count; i++)
            {
                if (i > 0)
                {
                    s.Append("; ");
                }
                s.Append(Args[i]);
            }
            s.Append(')');
        }
        s.Append(": ");
        Body.WriteTo(s);
        s.Append(';');
    }
}

internal sealed class Term
{
    public TermType Type;
    public IndexTerm? Index;
    public FuncCall? Func;
    public ObjectTerm? Object;
    public ArrayTerm? Array;
    public string Number = "";
    public Unary? Unary;
    public string Format = "";
    public StringTerm? Str;
    public IfTerm? If;
    public TryTerm? Try;
    public Reduce? Reduce;
    public Foreach? Foreach;
    public Label? Label;
    public string Break = "";
    public JqQuery? Query;
    public List<Suffix>? SuffixList;

    public Term Clone()
    {
        return (Term)MemberwiseClone();
    }

    public override string ToString()
    {
        StringBuilder s = new();
        WriteTo(s);
        return s.ToString();
    }

    public void WriteTo(StringBuilder s)
    {
        Jq.EnsureStack();
        switch (Type)
        {
            case TermType.Identity:
                s.Append('.');
                break;
            case TermType.Recurse:
                s.Append("..");
                break;
            case TermType.Null:
                s.Append("null");
                break;
            case TermType.True:
                s.Append("true");
                break;
            case TermType.False:
                s.Append("false");
                break;
            case TermType.Index:
                Index!.WriteTo(s);
                break;
            case TermType.Func:
                Func!.WriteTo(s);
                break;
            case TermType.Object:
                Object!.WriteTo(s);
                break;
            case TermType.Array:
                Array!.WriteTo(s);
                break;
            case TermType.Number:
                s.Append(Number);
                break;
            case TermType.Unary:
                Unary!.WriteTo(s);
                break;
            case TermType.Format:
                s.Append(Format);
                if (Str != null)
                {
                    s.Append(' ');
                    Str.WriteTo(s);
                }
                break;
            case TermType.String:
                Str!.WriteTo(s);
                break;
            case TermType.If:
                If!.WriteTo(s);
                break;
            case TermType.Try:
                Try!.WriteTo(s);
                break;
            case TermType.Reduce:
                Reduce!.WriteTo(s);
                break;
            case TermType.Foreach:
                Foreach!.WriteTo(s);
                break;
            case TermType.Label:
                Label!.WriteTo(s);
                break;
            case TermType.Break:
                s.Append("break ");
                s.Append(Break);
                break;
            case TermType.Query:
                s.Append('(');
                Query!.WriteTo(s);
                s.Append(')');
                break;
        }
        if (SuffixList != null)
        {
            foreach (Suffix e in SuffixList)
            {
                e.WriteTo(s);
            }
        }
    }

    public object? ToIndexKey()
    {
        if (SuffixList != null && SuffixList.Count > 0)
        {
            return null;
        }
        switch (Type)
        {
            case TermType.Number:
                return Jq.ToNumber(Number);
            case TermType.Unary:
                return Unary!.ToNumber();
            case TermType.String:
                return Str!.Queries == null ? Str.Str : null;
            default:
                return null;
        }
    }

    public List<object?>? ToIndices(List<object?>? xs)
    {
        switch (Type)
        {
            case TermType.Index:
                if ((xs = Index!.ToIndices(xs)) == null)
                {
                    return null;
                }
                break;
            case TermType.Query:
                if ((xs = Query!.ToIndices(xs)) == null)
                {
                    return null;
                }
                break;
            default:
                return null;
        }
        if (SuffixList != null)
        {
            foreach (Suffix s in SuffixList)
            {
                if ((xs = s.ToIndices(xs)) == null)
                {
                    return null;
                }
            }
        }
        return xs;
    }

    public object? ToNumber()
    {
        if (Type == TermType.Number && (SuffixList == null || SuffixList.Count == 0))
        {
            return Jq.ToNumber(Number);
        }
        return null;
    }
}

internal sealed class Unary
{
    public Operator Op;
    public Term Term = null!;

    public override string ToString()
    {
        StringBuilder s = new();
        WriteTo(s);
        return s.ToString();
    }

    public void WriteTo(StringBuilder s)
    {
        s.Append(Op.ToToken());
        Term.WriteTo(s);
    }

    public object? ToNumber()
    {
        object? v = Term.ToNumber();
        if (v != null && Op == Operator.Sub)
        {
            v = Jq.FuncOpNegate(v);
        }
        return v;
    }
}

internal sealed class Pattern
{
    public string Name = "";
    public List<Pattern>? Array;
    public List<PatternObject>? Object;

    public override string ToString()
    {
        StringBuilder s = new();
        WriteTo(s);
        return s.ToString();
    }

    public void WriteTo(StringBuilder s)
    {
        if (Name != "")
        {
            s.Append(Name);
        }
        else if (Array != null && Array.Count > 0)
        {
            s.Append('[');
            for (int i = 0; i < Array.Count; i++)
            {
                if (i > 0)
                {
                    s.Append(", ");
                }
                Array[i].WriteTo(s);
            }
            s.Append(']');
        }
        else if (Object != null && Object.Count > 0)
        {
            s.Append('{');
            for (int i = 0; i < Object.Count; i++)
            {
                if (i > 0)
                {
                    s.Append(", ");
                }
                Object[i].WriteTo(s);
            }
            s.Append('}');
        }
    }

    public static void WritePatternsTo(StringBuilder s, List<Pattern>? ps)
    {
        if (ps == null)
        {
            return;
        }
        for (int i = 0; i < ps.Count; i++)
        {
            s.Append(i == 0 ? " as " : " ?// ");
            ps[i].WriteTo(s);
        }
    }
}

internal sealed class PatternObject
{
    public string Key = "";
    public StringTerm? KeyString;
    public JqQuery? KeyQuery;
    public Pattern? Val;

    public void WriteTo(StringBuilder s)
    {
        if (Key != "")
        {
            s.Append(Key);
        }
        else if (KeyString != null)
        {
            KeyString.WriteTo(s);
        }
        else if (KeyQuery != null)
        {
            s.Append('(');
            KeyQuery.WriteTo(s);
            s.Append(')');
        }
        if (Val != null)
        {
            s.Append(": ");
            Val.WriteTo(s);
        }
    }
}

internal sealed class IndexTerm
{
    public string Name = "";
    public StringTerm? Str;
    public JqQuery? Start;
    public JqQuery? End;
    public bool IsSlice;

    public override string ToString()
    {
        StringBuilder s = new();
        WriteTo(s);
        return s.ToString();
    }

    public void WriteTo(StringBuilder s)
    {
        if (s.Length > 0)
        {
            // ". .x" != "..x" and "0 .x" != "0.x"
            char c = s[s.Length - 1];
            if (c == '.' || (c >= '0' && c <= '9'))
            {
                s.Append(' ');
            }
        }
        s.Append('.');
        WriteSuffixTo(s);
    }

    public void WriteSuffixTo(StringBuilder s)
    {
        if (Name != "")
        {
            s.Append(Name);
        }
        else if (Str != null)
        {
            Str.WriteTo(s);
        }
        else
        {
            s.Append('[');
            if (IsSlice)
            {
                Start?.WriteTo(s);
                s.Append(':');
                End?.WriteTo(s);
            }
            else
            {
                Start!.WriteTo(s);
            }
            s.Append(']');
        }
    }

    public object? ToIndexKey()
    {
        if (Name != "")
        {
            return Name;
        }
        if (Str != null)
        {
            return Str.Queries == null ? Str.Str : null;
        }
        if (!IsSlice)
        {
            return Start!.ToIndexKey();
        }
        object? start = null;
        object? end = null;
        bool ok = true;
        if (Start != null)
        {
            start = Start.ToIndexKey();
            ok = start != null;
        }
        if (End != null && ok)
        {
            end = End.ToIndexKey();
            ok = end != null;
        }
        if (ok)
        {
            return new Dictionary<string, object?> { ["start"] = start, ["end"] = end };
        }
        return null;
    }

    public List<object?>? ToIndices(List<object?>? xs)
    {
        object? k = ToIndexKey();
        if (k != null)
        {
            xs ??= new List<object?>();
            xs.Add(k);
            return xs;
        }
        return null;
    }
}

internal sealed class FuncCall
{
    public string Name = "";
    public List<JqQuery>? Args;

    public int ArgCount => Args?.Count ?? 0;

    public override string ToString()
    {
        StringBuilder s = new();
        WriteTo(s);
        return s.ToString();
    }

    public void WriteTo(StringBuilder s)
    {
        s.Append(Name);
        if (Args != null && Args.Count > 0)
        {
            s.Append('(');
            for (int i = 0; i < Args.Count; i++)
            {
                if (i > 0)
                {
                    s.Append("; ");
                }
                Args[i].WriteTo(s);
            }
            s.Append(')');
        }
    }
}

internal sealed class StringTerm
{
    public string Str = "";
    public List<JqQuery>? Queries;

    public void WriteTo(StringBuilder s)
    {
        if (Queries == null)
        {
            Encoder.EncodeString(s, Str);
            return;
        }
        s.Append('"');
        foreach (JqQuery e in Queries)
        {
            if (e.Term!.Str == null)
            {
                s.Append('\\');
                e.WriteTo(s);
            }
            else
            {
                string es = e.ToString();
                s.Append(es, 1, es.Length - 2);
            }
        }
        s.Append('"');
    }
}

internal sealed class ObjectTerm
{
    public List<ObjectKeyVal>? KeyVals;

    public void WriteTo(StringBuilder s)
    {
        if (KeyVals == null || KeyVals.Count == 0)
        {
            s.Append("{}");
            return;
        }
        s.Append("{ ");
        for (int i = 0; i < KeyVals.Count; i++)
        {
            if (i > 0)
            {
                s.Append(", ");
            }
            KeyVals[i].WriteTo(s);
        }
        s.Append(" }");
    }
}

internal sealed class ObjectKeyVal
{
    public string Key = "";
    public StringTerm? KeyString;
    public JqQuery? KeyQuery;
    public JqQuery? Val;

    public void WriteTo(StringBuilder s)
    {
        if (Key != "")
        {
            s.Append(Key);
        }
        else if (KeyString != null)
        {
            KeyString.WriteTo(s);
        }
        else if (KeyQuery != null)
        {
            s.Append('(');
            KeyQuery.WriteTo(s);
            s.Append(')');
        }
        if (Val != null)
        {
            s.Append(": ");
            Val.WriteTo(s);
        }
    }
}

internal sealed class ArrayTerm
{
    public JqQuery? Query;

    public void WriteTo(StringBuilder s)
    {
        s.Append('[');
        Query?.WriteTo(s);
        s.Append(']');
    }
}

internal sealed class Suffix
{
    public IndexTerm? Index;
    public bool Iter;
    public bool Optional;

    public override string ToString()
    {
        StringBuilder s = new();
        WriteTo(s);
        return s.ToString();
    }

    public void WriteTo(StringBuilder s)
    {
        if (Index != null)
        {
            if (Index.Name != "" || Index.Str != null)
            {
                Index.WriteTo(s);
            }
            else
            {
                Index.WriteSuffixTo(s);
            }
        }
        else if (Iter)
        {
            s.Append("[]");
        }
        else if (Optional)
        {
            s.Append('?');
        }
    }

    public Term? ToTerm()
    {
        if (Index != null)
        {
            return new Term { Type = TermType.Index, Index = Index };
        }
        if (Iter)
        {
            return new Term { Type = TermType.Identity, SuffixList = new List<Suffix> { new() { Iter = true } } };
        }
        return null;
    }

    public List<object?>? ToIndices(List<object?>? xs)
    {
        return Index?.ToIndices(xs);
    }
}

internal sealed class IfTerm
{
    public JqQuery Cond = null!;
    public JqQuery Then = null!;
    public List<IfElif>? Elif;
    public JqQuery? Else;

    public override string ToString()
    {
        StringBuilder s = new();
        WriteTo(s);
        return s.ToString();
    }

    public void WriteTo(StringBuilder s)
    {
        s.Append("if ");
        Cond.WriteTo(s);
        s.Append(" then ");
        Then.WriteTo(s);
        if (Elif != null)
        {
            foreach (IfElif e in Elif)
            {
                s.Append(' ');
                e.WriteTo(s);
            }
        }
        if (Else != null)
        {
            s.Append(" else ");
            Else.WriteTo(s);
        }
        s.Append(" end");
    }
}

internal sealed class IfElif
{
    public JqQuery Cond = null!;
    public JqQuery Then = null!;

    public void WriteTo(StringBuilder s)
    {
        s.Append("elif ");
        Cond.WriteTo(s);
        s.Append(" then ");
        Then.WriteTo(s);
    }
}

internal sealed class TryTerm
{
    public JqQuery Body = null!;
    public JqQuery? Catch;

    public override string ToString()
    {
        StringBuilder s = new();
        WriteTo(s);
        return s.ToString();
    }

    public void WriteTo(StringBuilder s)
    {
        s.Append("try ");
        Body.WriteTo(s);
        if (Catch != null)
        {
            s.Append(" catch ");
            Catch.WriteTo(s);
        }
    }
}

internal sealed class Reduce
{
    public JqQuery Query = null!;
    public List<Pattern> Patterns = null!;
    public JqQuery Start = null!;
    public JqQuery Update = null!;

    public override string ToString()
    {
        StringBuilder s = new();
        WriteTo(s);
        return s.ToString();
    }

    public void WriteTo(StringBuilder s)
    {
        s.Append("reduce ");
        Query.WriteTo(s);
        Pattern.WritePatternsTo(s, Patterns);
        s.Append(" (");
        Start.WriteTo(s);
        s.Append("; ");
        Update.WriteTo(s);
        s.Append(')');
    }
}

internal sealed class Foreach
{
    public JqQuery Query = null!;
    public List<Pattern> Patterns = null!;
    public JqQuery Start = null!;
    public JqQuery Update = null!;
    public JqQuery? Extract;

    public override string ToString()
    {
        StringBuilder s = new();
        WriteTo(s);
        return s.ToString();
    }

    public void WriteTo(StringBuilder s)
    {
        s.Append("foreach ");
        Query.WriteTo(s);
        Pattern.WritePatternsTo(s, Patterns);
        s.Append(" (");
        Start.WriteTo(s);
        s.Append("; ");
        Update.WriteTo(s);
        if (Extract != null)
        {
            s.Append("; ");
            Extract.WriteTo(s);
        }
        s.Append(')');
    }
}

internal sealed class Label
{
    public string Ident = "";
    public JqQuery Body = null!;

    public override string ToString()
    {
        StringBuilder s = new();
        WriteTo(s);
        return s.ToString();
    }

    public void WriteTo(StringBuilder s)
    {
        s.Append("label ");
        s.Append(Ident);
        s.Append(" | ");
        Body.WriteTo(s);
    }
}

internal sealed class ConstTerm
{
    public ConstObject? Object;
    public ConstArray? Array;
    public string Number = "";
    public string Str = "";
    public bool Null;
    public bool True;
    public bool False;

    public void WriteTo(StringBuilder s)
    {
        if (Object != null)
        {
            Object.WriteTo(s);
        }
        else if (Array != null)
        {
            Array.WriteTo(s);
        }
        else if (Number != "")
        {
            s.Append(Number);
        }
        else if (Null)
        {
            s.Append("null");
        }
        else if (True)
        {
            s.Append("true");
        }
        else if (False)
        {
            s.Append("false");
        }
        else
        {
            Encoder.EncodeString(s, Str);
        }
    }

    public object? ToValue()
    {
        if (Object != null)
        {
            return Object.ToValue();
        }
        if (Array != null)
        {
            return Array.ToValue();
        }
        if (Number != "")
        {
            return Jq.ToNumber(Number);
        }
        if (Null)
        {
            return null;
        }
        if (True)
        {
            return true;
        }
        if (False)
        {
            return false;
        }
        return Str;
    }

    public bool TryToString(out string str)
    {
        str = Str;
        return !(Object != null || Array != null || Number != "" || Null || True || False);
    }
}

internal sealed class ConstObject
{
    public List<ConstObjectKeyVal>? KeyVals;

    public void WriteTo(StringBuilder s)
    {
        if (KeyVals == null || KeyVals.Count == 0)
        {
            s.Append("{}");
            return;
        }
        s.Append("{ ");
        for (int i = 0; i < KeyVals.Count; i++)
        {
            if (i > 0)
            {
                s.Append(", ");
            }
            KeyVals[i].WriteTo(s);
        }
        s.Append(" }");
    }

    public Dictionary<string, object?> ToValue()
    {
        Dictionary<string, object?> v = new();
        if (KeyVals != null)
        {
            foreach (ConstObjectKeyVal e in KeyVals)
            {
                v[e.Key != "" ? e.Key : e.KeyString] = e.Val.ToValue();
            }
        }
        return v;
    }
}

internal sealed class ConstObjectKeyVal
{
    public string Key = "";
    public string KeyString = "";
    public ConstTerm Val = null!;

    public void WriteTo(StringBuilder s)
    {
        if (Key != "")
        {
            s.Append(Key);
        }
        else
        {
            Encoder.EncodeString(s, KeyString);
        }
        s.Append(": ");
        Val.WriteTo(s);
    }
}

internal sealed class ConstArray
{
    public List<ConstTerm>? Elems;

    public void WriteTo(StringBuilder s)
    {
        s.Append('[');
        if (Elems != null)
        {
            for (int i = 0; i < Elems.Count; i++)
            {
                if (i > 0)
                {
                    s.Append(", ");
                }
                Elems[i].WriteTo(s);
            }
        }
        s.Append(']');
    }

    public List<object?> ToValue()
    {
        List<object?> v = new(Elems?.Count ?? 0);
        if (Elems != null)
        {
            foreach (ConstTerm e in Elems)
            {
                v.Add(e.ToValue());
            }
        }
        return v;
    }
}
