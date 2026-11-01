namespace JqSharp;

// A stack whose popped blocks stay alive while a fork may still restore them (gojq stack.go).
internal sealed class ForkStack<T>
{
    public Block[] Data = new Block[16];
    public int Count;
    public int Index = -1;
    public int Limit = -1;

    public struct Block
    {
        public T Value;
        public int Next;
    }

    public void Push(T v)
    {
        Block b = new() { Value = v, Next = Index };
        Index = Math.Max(Index, Limit) + 1;
        if (Index >= Data.Length)
        {
            Array.Resize(ref Data, Data.Length * 2);
        }
        Data[Index] = b;
    }

    public T Pop()
    {
        Block b = Data[Index];
        Index = b.Next;
        return b.Value;
    }

    public T Top() => Data[Index].Value;

    public bool Empty() => Index < 0;

    public void Save(out int index, out int limit)
    {
        index = Index;
        limit = Limit;
        if (Index > Limit)
        {
            Limit = Index;
        }
    }

    public void Restore(int index, int limit)
    {
        Index = index;
        Limit = limit;
    }
}

internal struct Scope
{
    public int Id;
    public int Offset;
    public int Pc;
    public int SaveIndex;
    public int OuterIndex;
}

internal struct Fork
{
    public int Pc;
    public int StackIndex;
    public int StackLimit;
    public int ScopeIndex;
    public int ScopeLimit;
    public int PathIndex;
    public int PathLimit;
    public int Offset;
    public int ExpDepth;
}

internal readonly struct PathValue(object? path, object? value)
{
    public readonly object? Path = path;
    public readonly object? Value = value;
}

// The interpreter of the compiled code (gojq env.go and execute.go).
internal sealed class Env : IIter
{
    private readonly Code[] _codes;
    private readonly ForkStack<object?> _stack = new();
    private readonly ForkStack<PathValue> _paths = new();
    private readonly ForkStack<Scope> _scopes = new();
    private readonly List<Fork> _forks = new();
    private object?[] _values = Array.Empty<object?>();
    private int _pc;
    private bool _backtrack;
    private int _offset;
    private int _expdepth;
    private int _label;
    private readonly CancellationToken _token;
    private readonly bool _cancellable;

    public Env(Code[] codes, object? v, object?[] vars, CancellationToken token)
    {
        _codes = codes;
        _token = token;
        _cancellable = token.CanBeCanceled;
        _stack.Push(v);
        for (int i = vars.Length - 1; i >= 0; i--)
        {
            _stack.Push(vars[i]);
        }
    }

    // Returns the next output value or a JqException; false when the outputs are exhausted.
    public bool Next(out object? result)
    {
        JqException? err = null;
        int pc = _pc;
        int callpc = _codes.Length - 1;
        int index = -1;
        bool backtrack = _backtrack;
        try
        {
        loop:
            for (; pc < _codes.Length; pc++)
            {
                // Where gojq checks ctx.Done(): every instruction, so runaway recursion and loops stop too.
                if (_cancellable)
                {
                    _token.ThrowIfCancellationRequested();
                }
                Code code = _codes[pc];
                switch (code.Op)
                {
                    case Opcode.Nop:
                        break;
                    case Opcode.Push:
                        _stack.Push(code.V);
                        break;
                    case Opcode.Pop:
                        _stack.Pop();
                        break;
                    case Opcode.Dup:
                    {
                        object? v = _stack.Pop();
                        _stack.Push(v);
                        _stack.Push(v);
                        break;
                    }
                    case Opcode.Const:
                        _stack.Pop();
                        _stack.Push(code.V);
                        break;
                    case Opcode.Load:
                        _stack.Push(_values[IndexOf((VarRef)code.V!)]);
                        break;
                    case Opcode.Store:
                        _values[IndexOf((VarRef)code.V!)] = _stack.Pop();
                        break;
                    case Opcode.Object:
                    {
                        if (backtrack)
                        {
                            goto end;
                        }
                        int n = (int)code.V!;
                        Dictionary<string, object?> m = new(n);
                        for (int i = 0; i < n; i++)
                        {
                            object? v = _stack.Pop();
                            object? k = _stack.Pop();
                            if (k is not string s)
                            {
                                err = new ObjectKeyNotStringError(k);
                                goto end;
                            }
                            if (!m.ContainsKey(s))
                            {
                                m[s] = v;
                            }
                        }
                        _stack.Push(m);
                        break;
                    }
                    case Opcode.Append:
                    {
                        int i = IndexOf((VarRef)code.V!);
                        List<object?> list = (List<object?>)_values[i]!;
                        // the empty list is a shared constant; Go's append copies it, so do the same
                        if (list.Count == 0)
                        {
                            list = new List<object?>();
                            _values[i] = list;
                        }
                        list.Add(_stack.Pop());
                        break;
                    }
                    case Opcode.Fork:
                        if (backtrack)
                        {
                            if (err != null)
                            {
                                goto end;
                            }
                            pc = (int)code.V!;
                            backtrack = false;
                            goto loop;
                        }
                        PushFork(pc);
                        break;
                    case Opcode.ForkTryBegin:
                        if (backtrack)
                        {
                            switch (err)
                            {
                                case TryEndError e:
                                    err = e.Err;
                                    goto end;
                                case null or BreakError or JqHaltException:
                                    goto end;
                                case JqValueException e:
                                    _stack.Pop();
                                    _stack.Push(e.Value);
                                    break;
                                default:
                                    _stack.Pop();
                                    _stack.Push(err.Message);
                                    break;
                            }
                            pc = (int)code.V!;
                            backtrack = false;
                            err = null;
                            goto loop;
                        }
                        PushFork(pc);
                        break;
                    case Opcode.ForkTryEnd:
                        if (backtrack)
                        {
                            if (err is not (null or BreakError or JqHaltException))
                            {
                                err = new TryEndError(err);
                            }
                            goto end;
                        }
                        PushFork(pc);
                        break;
                    case Opcode.ForkAlt:
                        if (backtrack)
                        {
                            if (err is null or BreakError or JqHaltException)
                            {
                                goto end;
                            }
                            pc = (int)code.V!;
                            backtrack = false;
                            err = null;
                            goto loop;
                        }
                        PushFork(pc);
                        break;
                    case Opcode.ForkLabel:
                        if (backtrack)
                        {
                            object? label = _stack.Pop();
                            if (err is BreakError e && Equals(e.Value, label))
                            {
                                err = null;
                            }
                            goto end;
                        }
                        _stack.Push(_label);
                        PushFork(pc);
                        _stack.Pop();
                        _values[IndexOf((VarRef)code.V!)] = _label;
                        _label++;
                        break;
                    case Opcode.Backtrack:
                        goto end;
                    case Opcode.Jump:
                        pc = (int)code.V!;
                        goto loop;
                    case Opcode.JumpIfNot:
                    {
                        object? v = _stack.Pop();
                        if (v is null or false)
                        {
                            pc = (int)code.V!;
                            goto loop;
                        }
                        break;
                    }
                    case Opcode.Index:
                    case Opcode.IndexArray:
                    {
                        if (backtrack)
                        {
                            goto end;
                        }
                        object? p = code.V;
                        object? v = _stack.Pop();
                        if (code.Op == Opcode.IndexArray && v != null && v is not List<object?>)
                        {
                            err = new ExpectedArrayError(v);
                            goto end;
                        }
                        object? w = Jq.FuncIndex2(v, p);
                        if (w is JqException e)
                        {
                            err = e;
                            goto end;
                        }
                        _stack.Push(w);
                        if (!_paths.Empty() && _expdepth == 0)
                        {
                            if (!PathIntact(v))
                            {
                                err = new InvalidPathError(v);
                                goto end;
                            }
                            _paths.Push(new PathValue(p, w));
                        }
                        break;
                    }
                    case Opcode.Call:
                    {
                        if (backtrack)
                        {
                            goto end;
                        }
                        if (code.V is int target)
                        {
                            callpc = pc;
                            pc = target;
                            index = _scopes.Index;
                            goto loop;
                        }
                        CallFunc f = (CallFunc)code.V!;
                        object? x = _stack.Pop();
                        object?[] args = f.ArgCount == 0 ? Array.Empty<object?>() : new object?[f.ArgCount];
                        for (int i = 0; i < args.Length; i++)
                        {
                            args[i] = _stack.Pop();
                        }
                        object? w = f.Callback(x, args);
                        if (w is JqException e)
                        {
                            err = e;
                            goto end;
                        }
                        _stack.Push(w);
                        if (!_paths.Empty() && _expdepth == 0)
                        {
                            switch (f.Name)
                            {
                                case "_index":
                                    x = args[0];
                                    if (!PathIntact(x))
                                    {
                                        err = new InvalidPathError(x);
                                        goto end;
                                    }
                                    _paths.Push(new PathValue(args[1], w));
                                    break;
                                case "_slice":
                                    x = args[0];
                                    if (!PathIntact(x))
                                    {
                                        err = new InvalidPathError(x);
                                        goto end;
                                    }
                                    _paths.Push(new PathValue(new Dictionary<string, object?> { ["start"] = args[2], ["end"] = args[1] }, w));
                                    break;
                                case "getpath":
                                    if (!PathIntact(x))
                                    {
                                        err = new InvalidPathError(x);
                                        goto end;
                                    }
                                    foreach (object? p in (List<object?>)args[0]!)
                                    {
                                        _paths.Push(new PathValue(p, w));
                                    }
                                    break;
                            }
                        }
                        break;
                    }
                    case Opcode.CallRec:
                        pc = (int)code.V!;
                        callpc = -1;
                        index = _scopes.Index;
                        goto loop;
                    case Opcode.PushPc:
                        _stack.Push(new Closure((int)code.V!, _scopes.Index));
                        break;
                    case Opcode.CallPc:
                    {
                        Closure xs = (Closure)_stack.Pop()!;
                        callpc = pc;
                        pc = xs.Pc;
                        index = xs.ScopeIndex;
                        goto loop;
                    }
                    case Opcode.Scope:
                    {
                        ScopeRef xs = (ScopeRef)code.V!;
                        int saveindex;
                        int outerindex;
                        if (index == _scopes.Index)
                        {
                            if (callpc >= 0)
                            {
                                saveindex = index;
                            }
                            else
                            {
                                callpc = PopScope(out saveindex);
                            }
                        }
                        else
                        {
                            saveindex = _scopes.Index;
                        }
                        if ((outerindex = index) >= 0)
                        {
                            Scope s = _scopes.Data[outerindex].Value;
                            if (s.Id == xs.Id)
                            {
                                outerindex = s.OuterIndex;
                            }
                        }
                        _scopes.Push(new Scope { Id = xs.Id, Offset = _offset, Pc = callpc, SaveIndex = saveindex, OuterIndex = outerindex });
                        _offset += xs.Variables;
                        if (_offset > _values.Length)
                        {
                            Array.Resize(ref _values, _offset * 2);
                        }
                        break;
                    }
                    case Opcode.Ret:
                    {
                        if (backtrack)
                        {
                            goto end;
                        }
                        pc = PopScope(out int saveindex);
                        _scopes.Index = saveindex;
                        if (_scopes.Empty())
                        {
                            result = _stack.Pop();
                            return true;
                        }
                        break;
                    }
                    case Opcode.Iter:
                    {
                        if (err != null)
                        {
                            goto end;
                        }
                        backtrack = false;
                        ArraySegment<PathValue> xs;
                        object? top = _stack.Pop();
                        switch (top)
                        {
                            case ArraySegment<PathValue> rest:
                                xs = rest;
                                break;
                            case List<object?> a:
                            {
                                if (!_paths.Empty() && _expdepth == 0 && !PathIntact(a))
                                {
                                    err = new InvalidPathIterError(a);
                                    _stack.Push(EmptyIter.Instance);
                                    goto end;
                                }
                                if (a.Count == 0)
                                {
                                    goto end;
                                }
                                PathValue[] pvs = new PathValue[a.Count];
                                for (int i = 0; i < pvs.Length; i++)
                                {
                                    pvs[i] = new PathValue((long)i, a[i]);
                                }
                                xs = new ArraySegment<PathValue>(pvs);
                                break;
                            }
                            case Dictionary<string, object?> m:
                            {
                                if (!_paths.Empty() && _expdepth == 0 && !PathIntact(m))
                                {
                                    err = new InvalidPathIterError(m);
                                    _stack.Push(EmptyIter.Instance);
                                    goto end;
                                }
                                if (m.Count == 0)
                                {
                                    goto end;
                                }
                                List<string> keys = Jq.Keys(m);
                                PathValue[] pvs = new PathValue[keys.Count];
                                for (int i = 0; i < pvs.Length; i++)
                                {
                                    pvs[i] = new PathValue(keys[i], m[keys[i]]);
                                }
                                xs = new ArraySegment<PathValue>(pvs);
                                break;
                            }
                            case IIter it:
                            {
                                if (it.Next(out object? w))
                                {
                                    _stack.Push(it);
                                    PushFork(pc);
                                    _stack.Pop();
                                    if (w is JqException e)
                                    {
                                        err = e;
                                        goto end;
                                    }
                                    _stack.Push(w);
                                    continue;
                                }
                                goto end;
                            }
                            default:
                                err = new IteratorError(top);
                                _stack.Push(EmptyIter.Instance);
                                goto end;
                        }
                        if (xs.Count > 1)
                        {
                            _stack.Push(new ArraySegment<PathValue>(xs.Array!, xs.Offset + 1, xs.Count - 1));
                            PushFork(pc);
                            _stack.Pop();
                        }
                        PathValue first = xs.Array![xs.Offset];
                        _stack.Push(first.Value);
                        if (!_paths.Empty() && _expdepth == 0)
                        {
                            _paths.Push(first);
                        }
                        break;
                    }
                    case Opcode.ExpBegin:
                        _expdepth++;
                        break;
                    case Opcode.ExpEnd:
                        _expdepth--;
                        break;
                    case Opcode.PathBegin:
                        _paths.Push(new PathValue(null, _expdepth));
                        _paths.Push(new PathValue(null, _stack.Top()));
                        _expdepth = 0;
                        break;
                    case Opcode.PathEnd:
                    {
                        if (backtrack)
                        {
                            goto end;
                        }
                        _stack.Pop();
                        object? v = _stack.Pop();
                        if (!PathIntact(v))
                        {
                            err = new InvalidPathError(v);
                            goto end;
                        }
                        _stack.Push(PopPaths());
                        _expdepth = (int)_paths.Pop().Value!;
                        break;
                    }
                    default:
                        throw new InvalidOperationException("invalid opcode: " + code.Op);
                }
            }
        end:
            if (_forks.Count > 0)
            {
                pc = PopFork();
                backtrack = true;
                goto loop;
            }
            if (err != null)
            {
                result = err;
                return true;
            }
            result = null;
            return false;
        }
        finally
        {
            _pc = pc;
            _backtrack = true;
        }
    }

    private int PopScope(out int saveindex)
    {
        bool free = _scopes.Index > _scopes.Limit;
        Scope s = _scopes.Data[_scopes.Index].Value;
        _scopes.Pop();
        if (free)
        {
            _offset = s.Offset;
        }
        saveindex = s.SaveIndex;
        return s.Pc;
    }

    private void PushFork(int pc)
    {
        Fork f = new() { Pc = pc, Offset = _offset, ExpDepth = _expdepth };
        _stack.Save(out f.StackIndex, out f.StackLimit);
        _scopes.Save(out f.ScopeIndex, out f.ScopeLimit);
        _paths.Save(out f.PathIndex, out f.PathLimit);
        _forks.Add(f);
    }

    private int PopFork()
    {
        Fork f = _forks[_forks.Count - 1];
        _forks.RemoveAt(_forks.Count - 1);
        _offset = f.Offset;
        _expdepth = f.ExpDepth;
        _stack.Restore(f.StackIndex, f.StackLimit);
        _scopes.Restore(f.ScopeIndex, f.ScopeLimit);
        _paths.Restore(f.PathIndex, f.PathLimit);
        return f.Pc;
    }

    private int IndexOf(VarRef v)
    {
        for (int i = _scopes.Index; i >= 0;)
        {
            Scope s = _scopes.Data[i].Value;
            if (s.Id == v.Scope)
            {
                return s.Offset + v.Index;
            }
            i = s.OuterIndex;
        }
        throw new InvalidOperationException("env.index");
    }

    private bool PathIntact(object? v)
    {
        object? w = _paths.Top().Value;
        switch (v)
        {
            case List<object?> or Dictionary<string, object?>:
                if (w is List<object?> or Dictionary<string, object?>)
                {
                    return ReferenceEquals(v, w);
                }
                break;
            case double f:
                if (w is double g)
                {
                    return f == g || (double.IsNaN(f) && double.IsNaN(g));
                }
                break;
        }
        return Equals(v, w);
    }

    private List<object?> PopPaths()
    {
        List<object?> xs = new();
        while (true)
        {
            PathValue p = _paths.Pop();
            if (p.Path == null)
            {
                break;
            }
            xs.Add(p.Path);
        }
        xs.Reverse();
        return xs;
    }

    private sealed class EmptyIter : IIter
    {
        public static readonly EmptyIter Instance = new();

        public bool Next(out object? value)
        {
            value = null;
            return false;
        }
    }
}
