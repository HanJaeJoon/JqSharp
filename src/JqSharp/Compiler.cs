using System.Collections.Concurrent;

namespace JqSharp;

// Compiles a query to the bytecode Env runs (gojq compiler.go).
internal sealed class Compiler
{
    private sealed class ScopeInfo(int id)
    {
        public readonly List<VarInfo> Variables = new();
        public readonly List<FuncInfo> Funcs = new();
        public readonly int Id = id;
        public int Depth;
        public int VariableCnt;
    }

    private sealed class VarInfo(string name, VarRef index, int depth)
    {
        public readonly string Name = name;
        public readonly VarRef Index = index;
        public readonly int Depth = depth;
    }

    private sealed class FuncInfo(string name, int pc, int argcnt)
    {
        public string Name = name;
        public readonly int Pc = pc;
        public readonly int ArgCnt = argcnt;
    }

    private readonly IJqModuleLoader? _moduleLoader;
    private readonly Func<IEnumerable<string>>? _environLoader;
    private readonly IReadOnlyList<string> _variables;
    private readonly IEnumerator<object?>? _inputIter;
    private readonly object _inputLock = new();
    private readonly ConcurrentDictionary<(string, string), Jq.RegexMatcher> _regexpCache = new();
    private List<Code?> _codes = new();
    private ScopeInfo _builtinScope = null!;
    private List<ScopeInfo> _scopes = new();
    private int _scopecnt;

    private Compiler(JqCompilerOptions? options)
    {
        _moduleLoader = options?.ModuleLoader;
        _environLoader = options?.EnvironLoader;
        _variables = options?.Variables?.ToArray() ?? Array.Empty<string>();
        _inputIter = options?.InputIter?.GetEnumerator();
    }

    public static JqCode Compile(JqQuery q, JqCompilerOptions? options)
    {
        Compiler c = new(options);
        c._builtinScope = c.NewScope();
        ScopeInfo scope = c.NewScope();
        c._scopes = new List<ScopeInfo> { scope };
        Action setscope = c.Lazy(() => new Code(Opcode.Scope, new ScopeRef(scope.Id, scope.VariableCnt, 0)));
        foreach (string name in c._variables)
        {
            if (!new Lexer(name).ValidVarName())
            {
                throw new VariableNameError(name);
            }
            c.Append(new Code(Opcode.Store, c.PushVariable(name)));
        }
        if (c._moduleLoader != null)
        {
            foreach (JqQuery m in c._moduleLoader.LoadInitModules())
            {
                c.CompileModule(m, "");
            }
        }
        c.CompileRoot(q);
        setscope();
        c.OptimizeTailRec();
        c.OptimizeCodeOps();
        return new JqCode(c._variables, c._codes.Select(static x => x!).ToArray());
    }

    private void CompileRoot(JqQuery q)
    {
        if (q.Imports != null)
        {
            foreach (Import i in q.Imports)
            {
                CompileImport(i);
            }
        }
        CompileQuery(q);
        Append(new Code(Opcode.Ret));
    }

    private void CompileImport(Import i)
    {
        string path = i.ImportPath != "" ? i.ImportPath : i.IncludePath;
        string alias = i.ImportAlias;
        if (_moduleLoader == null)
        {
            throw new SimpleError("cannot load module: " + Encoder.Marshal(path));
        }
        if (alias.StartsWith("$", StringComparison.Ordinal))
        {
            object? vals = _moduleLoader.LoadJson(path, i.Meta?.ToValue());
            Append(new Code(Opcode.Push, vals));
            Append(new Code(Opcode.Store, PushVariable(alias)));
            Append(new Code(Opcode.Push, vals));
            Append(new Code(Opcode.Store, PushVariable(alias + "::" + alias.Substring(1))));
            return;
        }
        JqQuery q = _moduleLoader.LoadModule(path, i.Meta?.ToValue()) ?? throw ModuleNotFound(path);
        CompileModule(q, alias);
    }

    // A custom loader returning null instead of throwing.
    private static JqException ModuleNotFound(string name)
    {
        return new SimpleError("module not found: " + Encoder.Marshal(name));
    }

    private void CompileModule(JqQuery q, string alias)
    {
        ScopeInfo scope = _scopes[_scopes.Count - 1];
        scope.Depth++;
        int l = scope.Variables.Count;
        int m = scope.Funcs.Count;
        try
        {
            if (q.Imports != null)
            {
                foreach (Import i in q.Imports)
                {
                    CompileImport(i);
                }
            }
            if (q.FuncDefs != null)
            {
                foreach (FuncDef fd in q.FuncDefs)
                {
                    CompileFuncDef(fd, false);
                }
            }
        }
        finally
        {
            if (alias != "")
            {
                for (int k = m; k < scope.Funcs.Count; k++)
                {
                    scope.Funcs[k].Name = alias + "::" + scope.Funcs[k].Name;
                }
            }
            scope.Depth--;
            scope.Variables.RemoveRange(l, scope.Variables.Count - l);
        }
    }

    private VarRef NewVariable()
    {
        return CreateVariable("");
    }

    private VarRef PushVariable(string name)
    {
        ScopeInfo s = _scopes[_scopes.Count - 1];
        foreach (VarInfo v in s.Variables)
        {
            if (v.Name == name && v.Depth == s.Depth)
            {
                return v.Index;
            }
        }
        return CreateVariable(name);
    }

    private VarRef CreateVariable(string name)
    {
        ScopeInfo s = _scopes[_scopes.Count - 1];
        VarRef v = new(s.Id, s.VariableCnt);
        s.VariableCnt++;
        s.Variables.Add(new VarInfo(name, v, s.Depth));
        return v;
    }

    private bool LookupVariable(string name, out VarRef index)
    {
        for (int i = _scopes.Count - 1; i >= 0; i--)
        {
            ScopeInfo s = _scopes[i];
            for (int j = s.Variables.Count - 1; j >= 0; j--)
            {
                if (s.Variables[j].Name == name)
                {
                    index = s.Variables[j].Index;
                    return true;
                }
            }
        }
        index = default;
        return false;
    }

    private void LookupFuncOrVariable(string name, out FuncInfo? f, out VarInfo? v)
    {
        bool isFunc = name[0] != '$';
        for (int i = _scopes.Count - 1; i >= 0; i--)
        {
            ScopeInfo s = _scopes[i];
            if (isFunc)
            {
                for (int j = s.Funcs.Count - 1; j >= 0; j--)
                {
                    if (s.Funcs[j].Name == name && s.Funcs[j].ArgCnt == 0)
                    {
                        f = s.Funcs[j];
                        v = null;
                        return;
                    }
                }
            }
            for (int j = s.Variables.Count - 1; j >= 0; j--)
            {
                if (s.Variables[j].Name == name)
                {
                    f = null;
                    v = s.Variables[j];
                    return;
                }
            }
        }
        f = null;
        v = null;
    }

    private FuncInfo? LookupBuiltin(string name, int argcnt)
    {
        List<FuncInfo> funcs = _builtinScope.Funcs;
        for (int i = funcs.Count - 1; i >= 0; i--)
        {
            if (funcs[i].Name == name && funcs[i].ArgCnt == argcnt)
            {
                return funcs[i];
            }
        }
        return null;
    }

    private Action AppendBuiltin(string name, int argcnt)
    {
        Action setjump = Lazy(() => new Code(Opcode.Jump, _codes.Count));
        _builtinScope.Funcs.Add(new FuncInfo(name, _codes.Count, argcnt));
        return setjump;
    }

    private ScopeInfo NewScope()
    {
        // do not use the number of scopes, which pops
        return new ScopeInfo(_scopecnt++);
    }

    private Action NewScopeDepth()
    {
        ScopeInfo scope = _scopes[_scopes.Count - 1];
        int l = scope.Variables.Count;
        int m = scope.Funcs.Count;
        scope.Depth++;
        return () =>
        {
            scope.Depth--;
            scope.Variables.RemoveRange(l, scope.Variables.Count - l);
            scope.Funcs.RemoveRange(m, scope.Funcs.Count - m);
        };
    }

    private void CompileFuncDef(FuncDef e, bool builtin)
    {
        ScopeInfo scope = builtin ? _builtinScope : _scopes[_scopes.Count - 1];
        Action setjump = Lazy(() => new Code(Opcode.Jump, _codes.Count));
        scope.Funcs.Add(new FuncInfo(e.Name, _codes.Count, e.ArgCount));
        List<ScopeInfo> savedScopes = _scopes;
        int savedCount = _scopes.Count;
        try
        {
            scope = NewScope();
            if (builtin)
            {
                _scopes = new List<ScopeInfo> { _builtinScope, scope };
            }
            else
            {
                _scopes.Add(scope);
            }
            ScopeInfo funcScope = scope;
            Action setscope = Lazy(() => new Code(Opcode.Scope, new ScopeRef(funcScope.Id, funcScope.VariableCnt, e.ArgCount)));
            try
            {
                if (e.ArgCount > 0)
                {
                    List<(string Name, VarRef Index)> vis = new(e.ArgCount);
                    VarRef v = NewVariable();
                    Append(new Code(Opcode.Store, v));
                    foreach (string arg in e.Args!)
                    {
                        if (arg[0] == '$')
                        {
                            VarRef w = CreateVariable(arg.Substring(1));
                            Append(new Code(Opcode.Store, w));
                            vis.Add((arg, w));
                        }
                        else
                        {
                            Append(new Code(Opcode.Store, CreateVariable(arg)));
                        }
                    }
                    foreach ((string name, VarRef index) in vis)
                    {
                        Append(new Code(Opcode.Load, v));
                        Append(new Code(Opcode.ExpBegin));
                        Append(new Code(Opcode.Load, index));
                        Append(new Code(Opcode.CallPc));
                        Append(new Code(Opcode.Store, PushVariable(name)));
                        Append(new Code(Opcode.ExpEnd));
                    }
                    Append(new Code(Opcode.Load, v));
                }
                CompileRoot(e.Body);
            }
            finally
            {
                setscope();
            }
        }
        finally
        {
            _scopes = savedScopes;
            _scopes.RemoveRange(savedCount, _scopes.Count - savedCount);
            setjump();
        }
    }

    private void CompileQuery(JqQuery e)
    {
        Jq.EnsureStack();
        if (e.FuncDefs != null)
        {
            foreach (FuncDef fd in e.FuncDefs)
            {
                CompileFuncDef(fd, false);
            }
        }
        if (e.Term != null)
        {
            CompileTerm(e.Term);
            return;
        }
        switch (e.Op)
        {
            case 0:
                throw new SimpleError("missing query (try \".\")");
            case Operator.Pipe:
                if (e.Patterns != null && e.Patterns.Count > 0)
                {
                    CompileBind(e.Left!, e.Right!, e.Patterns);
                    return;
                }
                CompileQuery(e.Left!);
                CompileQuery(e.Right!);
                return;
            case Operator.Comma:
                CompileComma(e.Left!, e.Right!);
                return;
            case Operator.Alt:
                CompileAlt(e.Left!, e.Right!);
                return;
            case Operator.Assign:
            case Operator.Modify:
            case Operator.UpdateAdd:
            case Operator.UpdateSub:
            case Operator.UpdateMul:
            case Operator.UpdateDiv:
            case Operator.UpdateMod:
            case Operator.UpdateAlt:
                CompileQueryUpdate(e.Left!, e.Right!, e.Op);
                return;
            case Operator.Or:
                CompileIf(new IfTerm
                {
                    Cond = e.Left!,
                    Then = TermQuery(TermType.True),
                    Else = new JqQuery
                    {
                        Term = new Term
                        {
                            Type = TermType.If,
                            If = new IfTerm { Cond = e.Right!, Then = TermQuery(TermType.True), Else = TermQuery(TermType.False) },
                        },
                    },
                });
                return;
            case Operator.And:
                CompileIf(new IfTerm
                {
                    Cond = e.Left!,
                    Then = new JqQuery
                    {
                        Term = new Term
                        {
                            Type = TermType.If,
                            If = new IfTerm { Cond = e.Right!, Then = TermQuery(TermType.True), Else = TermQuery(TermType.False) },
                        },
                    },
                    Else = TermQuery(TermType.False),
                });
                return;
            default:
                CompileCall(e.Op.GetFunc(), new List<JqQuery> { e.Left!, e.Right! });
                return;
        }
    }

    private static JqQuery TermQuery(TermType type) => new() { Term = new Term { Type = type } };

    private static JqQuery FuncQuery(FuncCall f) => new() { Term = new Term { Type = TermType.Func, Func = f } };

    private void CompileComma(JqQuery l, JqQuery r)
    {
        Action setfork = Lazy(() => new Code(Opcode.Fork, _codes.Count));
        CompileQuery(l);
        Action setjump = Lazy(() => new Code(Opcode.Jump, _codes.Count));
        try
        {
            setfork();
            CompileQuery(r);
        }
        finally
        {
            setjump();
        }
    }

    private void CompileAlt(JqQuery l, JqQuery r)
    {
        Append(new Code(Opcode.Push, false));
        VarRef found = NewVariable();
        Append(new Code(Opcode.Store, found));
        Action setfork = Lazy(() => new Code(Opcode.Fork, _codes.Count)); // opload found
        CompileQuery(l);
        Append(new Code(Opcode.Dup));
        Append(new Code(Opcode.JumpIfNot, _codes.Count + 4)); // oppop
        Append(new Code(Opcode.Push, true)); // found some value
        Append(new Code(Opcode.Store, found));
        Action setjump = Lazy(() => new Code(Opcode.Jump, _codes.Count));
        try
        {
            Append(new Code(Opcode.Pop));
            Append(new Code(Opcode.Backtrack));
            setfork();
            Append(new Code(Opcode.Load, found));
            Append(new Code(Opcode.JumpIfNot, _codes.Count + 3));
            Append(new Code(Opcode.Backtrack)); // if found, backtrack
            Append(new Code(Opcode.Pop));
            CompileQuery(r);
        }
        finally
        {
            setjump();
        }
    }

    private void CompileQueryUpdate(JqQuery l, JqQuery r, Operator op)
    {
        switch (op)
        {
            case Operator.Assign:
            case Operator.Modify:
                if (op == Operator.Assign)
                {
                    // optimize assignment operator with constant indexing and slicing
                    //   .foo.[0].[1:2] = f => setpath(["foo",0,{"start":1,"end":2}]; f)
                    List<object?>? xs = l.ToIndices(null);
                    if (xs != null)
                    {
                        VarRef v = NewVariable();
                        Append(new Code(Opcode.Store, v));
                        Append(new Code(Opcode.Load, v));
                        CompileQuery(r);
                        Append(new Code(Opcode.Push, xs));
                        Append(new Code(Opcode.Load, v));
                        Append(new Code(Opcode.Call, new CallFunc(Jq.InternalFuncs["setpath"].Callback!, 2, "setpath")));
                        return;
                    }
                }
                CompileFunc(new FuncCall { Name = op.GetFunc(), Args = new List<JqQuery> { l, r } });
                return;
            default:
            {
                const string name = "$%0";
                Append(new Code(Opcode.Dup));
                CompileQuery(r);
                Append(new Code(Opcode.Store, PushVariable(name)));
                CompileFunc(new FuncCall
                {
                    Name = "_modify",
                    Args = new List<JqQuery>
                    {
                        l,
                        FuncQuery(new FuncCall
                        {
                            Name = op.GetFunc(),
                            Args = new List<JqQuery> { TermQuery(TermType.Identity), FuncQuery(new FuncCall { Name = name }) },
                        }),
                    },
                });
                return;
            }
        }
    }

    private void CompileBind(JqQuery l, JqQuery r, List<Pattern> patterns)
    {
        Action restore = NewScopeDepth();
        try
        {
            Append(new Code(Opcode.Dup));
            Append(new Code(Opcode.ExpBegin));
            CompileQuery(l);
            CompilePatterns(patterns, () =>
            {
                if (patterns.Count == 1 && _codes[_codes.Count - 2]!.Op == Opcode.ExpBegin)
                {
                    _codes[_codes.Count - 2]!.Op = Opcode.Nop;
                }
                else
                {
                    Append(new Code(Opcode.ExpEnd));
                }
                CompileQuery(r);
            });
        }
        finally
        {
            restore();
        }
    }

    private void CompilePatterns(List<Pattern> patterns, Action body)
    {
        int pc = 0;
        List<VarRef> vs = new();
        if (patterns.Count > 1)
        {
            foreach (Pattern p in patterns)
            {
                PushPatternVariables(vs, p);
            }
        }
        List<Action> defers = new();
        try
        {
            for (int i = 0; i < patterns.Count; i++)
            {
                int pcc = 0;
                if (i < patterns.Count - 1)
                {
                    defers.Add(Lazy(() => new Code(Opcode.ForkAlt, pcc)));
                }
                foreach (VarRef v in vs)
                {
                    Append(new Code(Opcode.Push, null));
                    Append(new Code(Opcode.Store, v));
                }
                CompilePattern(null, patterns[i]);
                if (i < patterns.Count - 1)
                {
                    defers.Add(Lazy(() => new Code(Opcode.Jump, pc)));
                    pcc = _codes.Count;
                }
            }
            if (patterns.Count > 1)
            {
                pc = _codes.Count;
            }
            body();
        }
        finally
        {
            for (int i = defers.Count - 1; i >= 0; i--)
            {
                defers[i]();
            }
        }
    }

    private void PushPatternVariables(List<VarRef> vs, Pattern p)
    {
        Jq.EnsureStack();
        if (p.Name != "")
        {
            VarRef v = PushVariable(p.Name);
            if (!vs.Contains(v))
            {
                vs.Add(v);
            }
        }
        else if (p.Array != null && p.Array.Count > 0)
        {
            foreach (Pattern q in p.Array)
            {
                PushPatternVariables(vs, q);
            }
        }
        else if (p.Object != null && p.Object.Count > 0)
        {
            foreach (PatternObject kv in p.Object)
            {
                if (kv.Key != "" && kv.Key[0] == '$')
                {
                    PushPatternVariables(vs, new Pattern { Name = kv.Key });
                }
                if (kv.Val != null)
                {
                    PushPatternVariables(vs, kv.Val);
                }
            }
        }
    }

    private void CompilePattern(List<VarRef>? vs, Pattern p)
    {
        Jq.EnsureStack();
        if (p.Name != "")
        {
            Append(new Code(Opcode.Store, PushVariable(p.Name)));
        }
        else if (p.Array != null && p.Array.Count > 0)
        {
            VarRef v = NewVariable();
            Append(new Code(Opcode.Store, v));
            for (int i = 0; i < p.Array.Count; i++)
            {
                Append(new Code(Opcode.Load, v));
                Append(new Code(Opcode.IndexArray, (long)i));
                CompilePattern(vs, p.Array[i]);
            }
        }
        else if (p.Object != null && p.Object.Count > 0)
        {
            VarRef v = NewVariable();
            Append(new Code(Opcode.Store, v));
            foreach (PatternObject kv in p.Object)
            {
                string key;
                string name = "";
                Append(new Code(Opcode.Load, v));
                if ((key = kv.Key) != "")
                {
                    if (key[0] == '$')
                    {
                        name = key;
                        key = key.Substring(1);
                    }
                }
                else if (kv.KeyString != null)
                {
                    if ((key = kv.KeyString.Str) == "")
                    {
                        CompileString(kv.KeyString, null);
                    }
                }
                else if (kv.KeyQuery != null)
                {
                    CompileQuery(kv.KeyQuery);
                }
                if (key != "")
                {
                    Append(new Code(Opcode.Index, key));
                }
                else
                {
                    Append(new Code(Opcode.Load, v));
                    Append(new Code(Opcode.Push, null));
                    // ref: CompileCall
                    Append(new Code(Opcode.Call, new CallFunc(Jq.InternalFuncs["_index"].Callback!, 2, "_index")));
                }
                if (name != "")
                {
                    if (kv.Val != null)
                    {
                        Append(new Code(Opcode.Dup));
                    }
                    CompilePattern(vs, new Pattern { Name = name });
                }
                if (kv.Val != null)
                {
                    CompilePattern(vs, kv.Val);
                }
            }
        }
        else
        {
            throw new SimpleError("invalid pattern: " + p);
        }
    }

    private void CompileIf(IfTerm e)
    {
        Append(new Code(Opcode.Dup)); // duplicate the value for then or else clause
        Append(new Code(Opcode.ExpBegin));
        int pc = _codes.Count;
        Action f = NewScopeDepth();
        CompileQuery(e.Cond);
        f();
        if (pc == _codes.Count)
        {
            _codes.RemoveAt(pc - 1);
        }
        else
        {
            Append(new Code(Opcode.ExpEnd));
        }
        int pcc = _codes.Count;
        Action setjumpifnot = Lazy(() => new Code(Opcode.JumpIfNot, _codes.Count)); // skip then clause
        f = NewScopeDepth();
        CompileQuery(e.Then);
        f();
        Action setjump = Lazy(() => new Code(Opcode.Jump, _codes.Count));
        try
        {
            setjumpifnot();
            if (e.Elif != null && e.Elif.Count > 0)
            {
                CompileIf(new IfTerm { Cond = e.Elif[0].Cond, Then = e.Elif[0].Then, Elif = e.Elif.GetRange(1, e.Elif.Count - 1), Else = e.Else });
                return;
            }
            if (e.Else != null)
            {
                Action restore = NewScopeDepth();
                try
                {
                    CompileQuery(e.Else);
                }
                finally
                {
                    restore();
                    // optimize constant results
                    //    opdup, ..., opjumpifnot, opconst, opjump, opconst
                    // => opnop, ..., opjumpifnot, oppush,  opjump, oppush
                    if (pcc + 4 == _codes.Count
                        && _codes[pcc + 1] != null && _codes[pcc + 1]!.Op == Opcode.Const
                        && _codes[pcc + 3] != null && _codes[pcc + 3]!.Op == Opcode.Const)
                    {
                        _codes[pc - 2]!.Op = Opcode.Nop;
                        _codes[pcc + 1]!.Op = Opcode.Push;
                        _codes[pcc + 3]!.Op = Opcode.Push;
                    }
                }
            }
        }
        finally
        {
            setjump();
        }
    }

    private void CompileTry(TryTerm e)
    {
        Action setforktrybegin = Lazy(() => new Code(Opcode.ForkTryBegin, _codes.Count));
        Action f = NewScopeDepth();
        CompileQuery(e.Body);
        f();
        Append(new Code(Opcode.ForkTryEnd));
        Action setjump = Lazy(() => new Code(Opcode.Jump, _codes.Count));
        try
        {
            setforktrybegin();
            if (e.Catch != null)
            {
                Action restore = NewScopeDepth();
                try
                {
                    CompileQuery(e.Catch);
                }
                finally
                {
                    restore();
                }
                return;
            }
            Append(new Code(Opcode.Backtrack));
        }
        finally
        {
            setjump();
        }
    }

    private void CompileReduce(Reduce e)
    {
        Action restore = NewScopeDepth();
        try
        {
            Append(new Code(Opcode.Dup));
            VarRef v = NewVariable();
            Action f = NewScopeDepth();
            CompileQuery(e.Start);
            f();
            Append(new Code(Opcode.Store, v));
            Action setfork = Lazy(() => new Code(Opcode.Fork, _codes.Count));
            CompileQuery(e.Query);
            CompilePatterns(e.Patterns, () =>
            {
                Append(new Code(Opcode.Load, v));
                Action g = NewScopeDepth();
                CompileQuery(e.Update);
                g();
                Append(new Code(Opcode.Store, v));
                Append(new Code(Opcode.Backtrack));
            });
            setfork();
            Append(new Code(Opcode.Pop));
            Append(new Code(Opcode.Load, v));
        }
        finally
        {
            restore();
        }
    }

    private void CompileForeach(Foreach e)
    {
        Action restore = NewScopeDepth();
        try
        {
            Append(new Code(Opcode.Dup));
            VarRef v = NewVariable();
            Action f = NewScopeDepth();
            CompileQuery(e.Start);
            f();
            Append(new Code(Opcode.Store, v));
            CompileQuery(e.Query);
            CompilePatterns(e.Patterns, () =>
            {
                Append(new Code(Opcode.Load, v));
                Action g = NewScopeDepth();
                CompileQuery(e.Update);
                g();
                Append(new Code(Opcode.Dup));
                Append(new Code(Opcode.Store, v));
                if (e.Extract != null)
                {
                    Action h = NewScopeDepth();
                    try
                    {
                        CompileQuery(e.Extract);
                    }
                    finally
                    {
                        h();
                    }
                }
            });
        }
        finally
        {
            restore();
        }
    }

    private void CompileLabel(Label e)
    {
        VarRef v = PushVariable("$%" + e.Ident.Substring(1));
        Append(new Code(Opcode.ForkLabel, v));
        CompileQuery(e.Body);
    }

    private void CompileBreak(string label)
    {
        if (!LookupVariable("$%" + label.Substring(1), out VarRef v))
        {
            throw new BreakError(label, null);
        }
        Append(new Code(Opcode.Pop));
        Append(new Code(Opcode.Load, v));
        Append(new Code(Opcode.Call, new CallFunc(FuncBreak(label), 0, "_break")));
    }

    private static Func<object?, object?[], object?> FuncBreak(string label)
    {
        return (v, _) => new BreakError(label, v);
    }

    private void CompileTerm(Term e)
    {
        Jq.EnsureStack();
        if (e.SuffixList != null && e.SuffixList.Count > 0)
        {
            Suffix s = e.SuffixList[e.SuffixList.Count - 1];
            Term t = e.Clone(); // clone without changing e
            t.SuffixList = e.SuffixList.GetRange(0, e.SuffixList.Count - 1);
            CompileTermSuffix(t, s);
            return;
        }
        switch (e.Type)
        {
            case TermType.Identity:
                return;
            case TermType.Recurse:
                CompileFunc(new FuncCall { Name = "recurse" });
                return;
            case TermType.Null:
                Append(new Code(Opcode.Const, null));
                return;
            case TermType.True:
                Append(new Code(Opcode.Const, true));
                return;
            case TermType.False:
                Append(new Code(Opcode.Const, false));
                return;
            case TermType.Index:
                CompileIndex(new Term { Type = TermType.Identity }, e.Index!);
                return;
            case TermType.Func:
                CompileFunc(e.Func!);
                return;
            case TermType.Object:
                CompileObject(e.Object!);
                return;
            case TermType.Array:
                CompileArray(e.Array!);
                return;
            case TermType.Number:
                Append(new Code(Opcode.Const, Jq.ToNumber(e.Number)));
                return;
            case TermType.Unary:
                CompileUnary(e.Unary!);
                return;
            case TermType.Format:
                CompileFormat(e.Format, e.Str);
                return;
            case TermType.String:
                CompileString(e.Str!, null);
                return;
            case TermType.If:
                CompileIf(e.If!);
                return;
            case TermType.Try:
                CompileTry(e.Try!);
                return;
            case TermType.Reduce:
                CompileReduce(e.Reduce!);
                return;
            case TermType.Foreach:
                CompileForeach(e.Foreach!);
                return;
            case TermType.Label:
                CompileLabel(e.Label!);
                return;
            case TermType.Break:
                CompileBreak(e.Break);
                return;
            case TermType.Query:
            {
                Action restore = NewScopeDepth();
                try
                {
                    CompileQuery(e.Query!);
                }
                finally
                {
                    restore();
                }
                return;
            }
            default:
                throw new InvalidOperationException("invalid term: " + e);
        }
    }

    private void CompileIndex(Term e, IndexTerm x)
    {
        object? k = x.ToIndexKey();
        if (k != null)
        {
            CompileTerm(e);
            Append(new Code(Opcode.Index, k));
            return;
        }
        JqQuery term = new() { Term = e };
        JqQuery nul = TermQuery(TermType.Null);
        if (x.Str != null)
        {
            CompileCall("_index", new List<JqQuery> { term, new() { Term = new Term { Type = TermType.String, Str = x.Str } } });
            return;
        }
        if (!x.IsSlice)
        {
            CompileCall("_index", new List<JqQuery> { term, x.Start! });
            return;
        }
        if (x.Start == null)
        {
            CompileCall("_slice", new List<JqQuery> { term, x.End!, nul });
            return;
        }
        if (x.End == null)
        {
            CompileCall("_slice", new List<JqQuery> { term, nul, x.Start });
            return;
        }
        CompileCall("_slice", new List<JqQuery> { term, x.End, x.Start });
    }

    private void CompileFunc(FuncCall e)
    {
        int argc = e.ArgCount;
        List<JqQuery> args = e.Args ?? new List<JqQuery>();
        if (argc == 0)
        {
            LookupFuncOrVariable(e.Name, out FuncInfo? f, out VarInfo? v);
            if (f != null)
            {
                CompileCallPc(f, args);
                return;
            }
            if (v != null)
            {
                if (e.Name[0] == '$')
                {
                    Append(new Code(Opcode.Pop));
                    Append(new Code(Opcode.Load, v.Index));
                }
                else
                {
                    Append(new Code(Opcode.Load, v.Index));
                    Append(new Code(Opcode.CallPc));
                }
                return;
            }
            if (e.Name == "$ENV" || e.Name == "env")
            {
                Dictionary<string, object?> env = new();
                if (_environLoader != null)
                {
                    foreach (string kv in _environLoader())
                    {
                        int i = kv.IndexOf('=');
                        if (i > 0)
                        {
                            env[kv.Substring(0, i)] = kv.Substring(i + 1);
                        }
                    }
                }
                Append(new Code(Opcode.Const, env));
                return;
            }
            if (e.Name[0] == '$')
            {
                throw new VariableNotFoundError(e.Name);
            }
        }
        else
        {
            for (int i = _scopes.Count - 1; i >= 0; i--)
            {
                ScopeInfo s = _scopes[i];
                for (int j = s.Funcs.Count - 1; j >= 0; j--)
                {
                    FuncInfo f = s.Funcs[j];
                    if (f.Name == e.Name && f.ArgCnt == argc)
                    {
                        CompileCallPc(f, args);
                        return;
                    }
                }
            }
        }
        FuncInfo? builtinFunc = LookupBuiltin(e.Name, argc);
        if (builtinFunc != null)
        {
            CompileCallPc(builtinFunc, args);
            return;
        }
        if (Builtin.FuncDefs.TryGetValue(e.Name, out List<FuncDef>? fds))
        {
            bool compiled = false;
            foreach (FuncDef fd in fds)
            {
                if (fd.ArgCount == argc)
                {
                    CompileFuncDef(fd, true);
                    compiled = true;
                    break;
                }
            }
            if (!compiled)
            {
                switch (e.Name)
                {
                    case "_assign":
                        CompileAssign();
                        break;
                    case "_modify":
                        CompileModify();
                        break;
                    case "_last":
                        CompileLast();
                        break;
                }
            }
            builtinFunc = LookupBuiltin(e.Name, argc);
            if (builtinFunc != null)
            {
                CompileCallPc(builtinFunc, args);
                return;
            }
        }
        if (Jq.InternalFuncs.TryGetValue(e.Name, out Function? fn) && fn.Accept(argc))
        {
            switch (e.Name)
            {
                case "empty":
                    Append(new Code(Opcode.Backtrack));
                    return;
                case "path":
                    Append(new Code(Opcode.PathBegin));
                    CompileCall(e.Name, args);
                    _codes[_codes.Count - 1] = new Code(Opcode.PathEnd);
                    return;
                case "builtins":
                    CompileCallInternal(new CallFunc(FuncBuiltins, 0, e.Name), args, true, -1);
                    return;
                case "input":
                    if (_inputIter == null)
                    {
                        throw new InputNotAllowedError();
                    }
                    CompileCallInternal(new CallFunc(FuncInput, 0, e.Name), args, true, -1);
                    return;
                case "modulemeta":
                    CompileCallInternal(new CallFunc(FuncModulemeta, 0, e.Name), args, true, -1);
                    return;
                case "debug":
                {
                    Action setfork = Lazy(() => new Code(Opcode.Fork, _codes.Count));
                    CompileQuery(args[0]);
                    try
                    {
                        CompileFunc(new FuncCall { Name = "debug" });
                    }
                    catch (FuncNotFoundError)
                    {
                        throw new FuncNotFoundError(e);
                    }
                    Append(new Code(Opcode.Backtrack));
                    setfork();
                    return;
                }
                case "_match":
                    CompileCallInternal(new CallFunc(FuncMatch, argc, e.Name), args, true, -1);
                    return;
                default:
                    CompileCall(e.Name, args);
                    return;
            }
        }
        throw new FuncNotFoundError(e);
    }

    // The assignment operator (=), compiled by hand to update values in place where it is safe:
    //   def _assign(p; $x): reduce path(p) as $q (.; setpath($q; $x));
    private void CompileAssign()
    {
        Action done = AppendBuiltin("_assign", 2);
        ScopeInfo scope = NewScope();
        VarRef v = new(scope.Id, 0), p = new(scope.Id, 1);
        VarRef x = new(scope.Id, 2), a = new(scope.Id, 3);
        // Cannot reuse v, p due to backtracking in x.
        VarRef w = new(scope.Id, 4), q = new(scope.Id, 5);
        int start = _codes.Count;
        Appends(
            new Code(Opcode.Scope, new ScopeRef(scope.Id, 6, 2)),
            new Code(Opcode.Store, v), // def _assign(p; $x):
            new Code(Opcode.Store, p),
            new Code(Opcode.Store, x),
            new Code(Opcode.Load, v),
            new Code(Opcode.ExpBegin),
            new Code(Opcode.Load, x),
            new Code(Opcode.CallPc),
            new Code(Opcode.Store, x),
            new Code(Opcode.ExpEnd),
            new Code(Opcode.Push, null),
            new Code(Opcode.Call, new CallFunc(Jq.FuncAllocator, 0, "_allocator")),
            new Code(Opcode.Store, a),
            new Code(Opcode.Load, v),
            new Code(Opcode.Fork, start + 30), // reduce [L1]
            new Code(Opcode.Dup),
            new Code(Opcode.Store, w),
            new Code(Opcode.PathBegin), // path(p)
            new Code(Opcode.Load, p),
            new Code(Opcode.CallPc),
            new Code(Opcode.Load, w),
            new Code(Opcode.PathEnd),
            new Code(Opcode.Store, q), // as $q (.;
            new Code(Opcode.Load, a), // setpath($q; $x)
            new Code(Opcode.Load, x),
            new Code(Opcode.Load, q),
            new Code(Opcode.Load, w),
            new Code(Opcode.Call, new CallFunc(Jq.FuncSetpathWithAllocator, 3, "_setpath")),
            new Code(Opcode.Store, w),
            new Code(Opcode.Backtrack), // );
            new Code(Opcode.Pop), // [L1]
            new Code(Opcode.Load, w),
            new Code(Opcode.Ret));
        done();
    }

    // The update-assignment operator (|=), compiled by hand like _assign/2.
    private void CompileModify()
    {
        Action done = AppendBuiltin("_modify", 2);
        ScopeInfo scope = NewScope();
        VarRef v = new(scope.Id, 0), p = new(scope.Id, 1);
        VarRef f = new(scope.Id, 2), d = new(scope.Id, 3);
        VarRef a = new(scope.Id, 4), l = new(scope.Id, 5);
        int start = _codes.Count;
        Appends(
            new Code(Opcode.Scope, new ScopeRef(scope.Id, 6, 2)),
            new Code(Opcode.Store, v), // def _modify(p; f):
            new Code(Opcode.Store, p),
            new Code(Opcode.Store, f),
            new Code(Opcode.Push, new List<object?>()),
            new Code(Opcode.Store, d),
            new Code(Opcode.Push, null),
            new Code(Opcode.Call, new CallFunc(Jq.FuncAllocator, 0, "_allocator")),
            new Code(Opcode.Store, a),
            new Code(Opcode.Load, v),
            new Code(Opcode.Fork, start + 40), // reduce [L1]
            new Code(Opcode.PathBegin), // path(p)
            new Code(Opcode.Load, p),
            new Code(Opcode.CallPc),
            new Code(Opcode.Load, v),
            new Code(Opcode.PathEnd),
            new Code(Opcode.Store, p), // as $p (.;
            new Code(Opcode.ForkLabel, l), // label $l |
            new Code(Opcode.Load, v),
            new Code(Opcode.Fork, start + 37), // [L2]
            new Code(Opcode.Pop), // (getpath($p) |
            new Code(Opcode.Load, a),
            new Code(Opcode.Load, a),
            new Code(Opcode.Load, p),
            new Code(Opcode.Load, v),
            new Code(Opcode.Call, new CallFunc(Jq.FuncGetpathWithAllocator, 2, "getpath")),
            new Code(Opcode.Load, f), // f)
            new Code(Opcode.CallPc),
            new Code(Opcode.Load, p), // setpath($p; ...)
            new Code(Opcode.Load, v),
            new Code(Opcode.Call, new CallFunc(Jq.FuncSetpathWithAllocator, 3, "_setpath")),
            new Code(Opcode.Store, v),
            new Code(Opcode.Load, v), // ., break $l
            new Code(Opcode.Fork, start + 35), // [L4]
            new Code(Opcode.Jump, start + 39), // [L3]
            new Code(Opcode.Load, l), // [L4]
            new Code(Opcode.Call, new CallFunc(FuncBreak(""), 0, "_break")),
            new Code(Opcode.Load, p), // append $p to $d [L2]
            new Code(Opcode.Append, d),
            new Code(Opcode.Backtrack), // ) | [L3]
            new Code(Opcode.Pop), // delpaths($d); [L1]
            new Code(Opcode.Load, a),
            new Code(Opcode.Load, d),
            new Code(Opcode.Load, v),
            new Code(Opcode.Call, new CallFunc(Jq.FuncDelpathsWithAllocator, 2, "_delpaths")),
            new Code(Opcode.Ret));
        done();
    }

    // last/1, compiled by hand to avoid unnecessary boxing.
    private void CompileLast()
    {
        Action done = AppendBuiltin("_last", 1);
        ScopeInfo scope = NewScope();
        VarRef v = new(scope.Id, 0), g = new(scope.Id, 1), x = new(scope.Id, 2);
        int start = _codes.Count;
        Appends(
            new Code(Opcode.Scope, new ScopeRef(scope.Id, 3, 1)),
            new Code(Opcode.Store, v),
            new Code(Opcode.Store, g),
            new Code(Opcode.Push, true), // $x = true
            new Code(Opcode.Store, x),
            new Code(Opcode.Load, v),
            new Code(Opcode.Fork, start + 13), // reduce [L1]
            new Code(Opcode.Load, g), // g
            new Code(Opcode.CallPc),
            new Code(Opcode.Store, v), // as $v (
            new Code(Opcode.Push, false), // $x = false
            new Code(Opcode.Store, x),
            new Code(Opcode.Backtrack), // );
            new Code(Opcode.Pop), // [L1]
            new Code(Opcode.Load, x), // if $x then $v else empty end
            new Code(Opcode.JumpIfNot, start + 17),
            new Code(Opcode.Backtrack),
            new Code(Opcode.Load, v),
            new Code(Opcode.Ret));
        done();
    }

    private object? FuncBuiltins(object? v, object?[] args)
    {
        List<(string Name, int Arity)> xs = new();
        foreach (KeyValuePair<string, List<FuncDef>> kv in Builtin.FuncDefs)
        {
            foreach (FuncDef fd in kv.Value)
            {
                if (fd.Name[0] != '_')
                {
                    xs.Add((fd.Name, fd.ArgCount));
                }
            }
        }
        foreach (KeyValuePair<string, Function> kv in Jq.InternalFuncs)
        {
            if (kv.Key[0] != '_')
            {
                for (int i = 0, cnt = kv.Value.ArgCount; cnt > 0; i++, cnt >>= 1)
                {
                    if ((cnt & 1) > 0)
                    {
                        xs.Add((kv.Key, i));
                    }
                }
            }
        }
        xs.Sort(static (x, y) =>
        {
            int c = Jq.CompareStrings(x.Name, y.Name);
            return c != 0 ? c : x.Arity.CompareTo(y.Arity);
        });
        List<object?> ys = new(xs.Count);
        foreach ((string name, int arity) in xs)
        {
            ys.Add(name + "/" + arity);
        }
        return ys;
    }

    private object? FuncInput(object? v, object?[] args)
    {
        lock (_inputLock)
        {
            try
            {
                if (!_inputIter!.MoveNext())
                {
                    return new SimpleError("break");
                }
                return JqValue.Normalize(_inputIter.Current);
            }
            catch (JqException err)
            {
                return err;
            }
        }
    }

    private object? FuncModulemeta(object? v, object?[] args)
    {
        if (v is not string s)
        {
            return new Func0TypeError("modulemeta", v);
        }
        if (_moduleLoader == null)
        {
            return new SimpleError("cannot load module: " + Encoder.Marshal(s));
        }
        JqQuery q;
        try
        {
            q = _moduleLoader.LoadModule(s, null) ?? throw ModuleNotFound(s);
        }
        catch (JqException err)
        {
            return err;
        }
        Dictionary<string, object?> meta = q.Meta?.ToValue() ?? new Dictionary<string, object?>();
        meta["defs"] = ListModuleDefs(q);
        meta["deps"] = ListModuleDeps(q);
        return meta;
    }

    private static List<object?> ListModuleDefs(JqQuery q)
    {
        List<(string Name, int Arity)> xs = new();
        if (q.FuncDefs != null)
        {
            foreach (FuncDef fd in q.FuncDefs)
            {
                if (fd.Name[0] != '_')
                {
                    xs.Add((fd.Name, fd.ArgCount));
                }
            }
        }
        xs.Sort(static (x, y) =>
        {
            int c = Jq.CompareStrings(x.Name, y.Name);
            return c != 0 ? c : x.Arity.CompareTo(y.Arity);
        });
        List<object?> defs = new(xs.Count);
        foreach ((string name, int arity) in xs)
        {
            defs.Add(name + "/" + arity);
        }
        return defs;
    }

    private static List<object?> ListModuleDeps(JqQuery q)
    {
        List<object?> deps = new();
        if (q.Imports != null)
        {
            foreach (Import i in q.Imports)
            {
                Dictionary<string, object?> v = i.Meta?.ToValue() ?? new Dictionary<string, object?>();
                v["relpath"] = i.ImportPath != "" ? i.ImportPath : i.IncludePath;
                if (i.ImportAlias != "")
                {
                    v["as"] = i.ImportAlias.StartsWith("$", StringComparison.Ordinal) ? i.ImportAlias.Substring(1) : i.ImportAlias;
                }
                v["is_data"] = i.ImportAlias.StartsWith("$", StringComparison.Ordinal);
                deps.Add(v);
            }
        }
        return deps;
    }

    private object? FuncMatch(object? v, object?[] args)
    {
        return Jq.FuncMatch(v, args[0], args[1], args[2], _regexpCache);
    }

    private void CompileObject(ObjectTerm e)
    {
        if (e.KeyVals == null || e.KeyVals.Count == 0)
        {
            Append(new Code(Opcode.Const, new Dictionary<string, object?>()));
            return;
        }
        Action restore = NewScopeDepth();
        try
        {
            VarRef v = NewVariable();
            Append(new Code(Opcode.Store, v));
            int pc = _codes.Count;
            foreach (ObjectKeyVal kv in e.KeyVals)
            {
                CompileObjectKeyVal(v, kv);
            }
            Append(new Code(Opcode.Object, e.KeyVals.Count));
            // optimize constant objects
            int l = e.KeyVals.Count;
            if (pc + (l * 3) + 1 != _codes.Count)
            {
                return;
            }
            for (int i = 0; i < l; i++)
            {
                if (_codes[pc + (i * 3)]!.Op != Opcode.Push
                    || _codes[pc + (i * 3) + 1]!.Op != Opcode.Load
                    || _codes[pc + (i * 3) + 2]!.Op != Opcode.Const)
                {
                    return;
                }
            }
            Dictionary<string, object?> w = new(l);
            for (int i = 0; i < l; i++)
            {
                w[(string)_codes[pc + (i * 3)]!.V!] = _codes[pc + (i * 3) + 2]!.V;
            }
            _codes[pc - 1] = new Code(Opcode.Const, w);
            _codes.RemoveRange(pc, _codes.Count - pc);
        }
        finally
        {
            restore();
        }
    }

    private void CompileObjectKeyVal(VarRef v, ObjectKeyVal kv)
    {
        if (kv.Key != "")
        {
            string key = kv.Key;
            if (key[0] == '$')
            {
                if (kv.Val == null)
                {
                    // {$foo} == {foo:$foo}
                    Append(new Code(Opcode.Push, key.Substring(1)));
                }
                Append(new Code(Opcode.Load, v));
                CompileFunc(new FuncCall { Name = key });
            }
            else
            {
                Append(new Code(Opcode.Push, key));
                if (kv.Val == null)
                {
                    // {foo} == {foo:.foo}
                    Append(new Code(Opcode.Load, v));
                    Append(new Code(Opcode.Index, key));
                }
            }
        }
        else if (kv.KeyString != null)
        {
            StringTerm key = kv.KeyString;
            if (key.Queries == null)
            {
                Append(new Code(Opcode.Push, key.Str));
                if (kv.Val == null)
                {
                    // {"foo"} == {"foo":.["foo"]}
                    Append(new Code(Opcode.Load, v));
                    Append(new Code(Opcode.Index, key.Str));
                }
            }
            else
            {
                Append(new Code(Opcode.Load, v));
                CompileString(key, null);
                if (kv.Val == null)
                {
                    Append(new Code(Opcode.Dup));
                    Append(new Code(Opcode.Load, v));
                    Append(new Code(Opcode.Push, null));
                    // ref: CompileCall
                    Append(new Code(Opcode.Call, new CallFunc(Jq.InternalFuncs["_index"].Callback!, 2, "_index")));
                }
            }
        }
        else if (kv.KeyQuery != null)
        {
            Append(new Code(Opcode.Load, v));
            Action f = NewScopeDepth();
            CompileQuery(kv.KeyQuery);
            f();
        }
        if (kv.Val != null)
        {
            Append(new Code(Opcode.Load, v));
            CompileQuery(kv.Val);
        }
    }

    private void CompileArray(ArrayTerm e)
    {
        if (e.Query == null)
        {
            Append(new Code(Opcode.Const, new List<object?>()));
            return;
        }
        Append(new Code(Opcode.Push, new List<object?>()));
        VarRef arr = NewVariable();
        Append(new Code(Opcode.Store, arr));
        int pc = _codes.Count;
        Action setfork = Lazy(() => new Code(Opcode.Fork, _codes.Count));
        Action restore = NewScopeDepth();
        try
        {
            CompileQuery(e.Query);
            Append(new Code(Opcode.Append, arr));
            Append(new Code(Opcode.Backtrack));
            setfork();
            Append(new Code(Opcode.Pop));
            Append(new Code(Opcode.Load, arr));
            if (e.Query.Op == Operator.Pipe)
            {
                return;
            }
            // optimize constant arrays
            if ((_codes.Count - pc) % 3 != 0)
            {
                return;
            }
            int l = (_codes.Count - pc - 3) / 3;
            for (int i = 0; i < l; i++)
            {
                if (_codes[pc + i]!.Op != Opcode.Fork
                    || _codes[pc + (i * 2) + l]!.Op != Opcode.Const
                    || (i < l - 1 && _codes[pc + (i * 2) + l + 1]!.Op != Opcode.Jump))
                {
                    return;
                }
            }
            List<object?> v = new(l);
            for (int i = 0; i < l; i++)
            {
                v.Add(_codes[pc + (i * 2) + l]!.V);
            }
            _codes[pc - 2] = new Code(Opcode.Const, v);
            _codes.RemoveRange(pc - 1, _codes.Count - (pc - 1));
        }
        finally
        {
            restore();
        }
    }

    private void CompileUnary(Unary e)
    {
        object? v = e.ToNumber();
        if (v != null)
        {
            Append(new Code(Opcode.Const, v));
            return;
        }
        CompileTerm(e.Term);
        switch (e.Op)
        {
            case Operator.Add:
                CompileCall("_plus", new List<JqQuery>());
                return;
            case Operator.Sub:
                CompileCall("_negate", new List<JqQuery>());
                return;
            default:
                throw new SimpleError("unexpected operator in Unary: " + e.Op.ToToken());
        }
    }

    private void CompileFormat(string format, StringTerm? str)
    {
        FuncCall f = FormatToFunc(format) ?? new FuncCall
        {
            Name = "format",
            Args = new List<JqQuery> { new() { Term = new Term { Type = TermType.String, Str = new StringTerm { Str = format.Substring(1) } } } },
        };
        if (str == null)
        {
            CompileFunc(f);
            return;
        }
        CompileString(str, f);
    }

    internal static FuncCall? FormatToFunc(string format)
    {
        string? name = format switch
        {
            "@text" => "tostring",
            "@json" => "tojson",
            "@html" => "_tohtml",
            "@uri" => "_touri",
            "@urid" => "_tourid",
            "@csv" => "_tocsv",
            "@tsv" => "_totsv",
            "@sh" => "_tosh",
            "@base64" => "_tobase64",
            "@base64d" => "_tobase64d",
            _ => null,
        };
        return name == null ? null : new FuncCall { Name = name };
    }

    private void CompileString(StringTerm s, FuncCall? f)
    {
        if (s.Queries == null)
        {
            Append(new Code(Opcode.Const, s.Str));
            return;
        }
        f ??= new FuncCall { Name = "tostring" };
        JqQuery? q = null;
        foreach (JqQuery part in s.Queries)
        {
            JqQuery e = part;
            if (e.Term!.Str == null)
            {
                e = new JqQuery { Left = e, Op = Operator.Pipe, Right = FuncQuery(f) };
            }
            q = q == null ? e : new JqQuery { Left = q, Op = Operator.Add, Right = e };
        }
        if (q == null)
        {
            // an empty interpolated string, which gojq compiles as a missing query
            throw new SimpleError("missing query (try \".\")");
        }
        CompileQuery(q);
    }

    private void CompileTermSuffix(Term e, Suffix s)
    {
        if (s.Index != null)
        {
            CompileIndex(e, s.Index);
            return;
        }
        if (s.Iter)
        {
            CompileTerm(e);
            Append(new Code(Opcode.Iter));
            return;
        }
        if (s.Optional)
        {
            if (e.SuffixList != null && e.SuffixList.Count > 0)
            {
                Term? u = e.SuffixList[e.SuffixList.Count - 1].ToTerm();
                if (u != null)
                {
                    // no need to clone (ref: CompileTerm)
                    e.SuffixList = e.SuffixList.GetRange(0, e.SuffixList.Count - 1);
                    CompileTerm(e);
                    e = u;
                }
            }
            CompileTry(new TryTerm { Body = new JqQuery { Term = e } });
            return;
        }
        throw new SimpleError("invalid suffix: " + s);
    }

    private void CompileCall(string name, List<JqQuery> args)
    {
        Function fn = Jq.InternalFuncs[name];
        int indexing = name switch
        {
            "_index" or "_slice" => 1,
            "getpath" => 0,
            _ => -1,
        };
        CompileCallInternal(fn.Callback == null ? null : new CallFunc(fn.Callback, args.Count, name), args, true, indexing);
        if (fn.Iter)
        {
            Append(new Code(Opcode.Iter));
        }
    }

    private void CompileCallPc(FuncInfo fn, List<JqQuery> args)
    {
        CompileCallInternal(fn.Pc, args, false, -1);
    }

    private void CompileCallInternal(object? fn, List<JqQuery> args, bool @internal, int indexing)
    {
        if (args.Count == 0)
        {
            Append(new Code(Opcode.Call, fn));
            return;
        }
        VarRef v = NewVariable();
        Append(new Code(Opcode.Store, v));
        if (indexing >= 0)
        {
            Append(new Code(Opcode.ExpBegin));
        }
        for (int i = args.Count - 1; i >= 0; i--)
        {
            int pc = _codes.Count + 1; // skip opjump (ref: CompileFuncDef)
            string name = "lambda:" + pc;
            CompileFuncDef(new FuncDef { Name = name, Body = args[i] }, false);
            if (@internal)
            {
                switch (_codes.Count - pc)
                {
                    case 2: // optimize identity argument (opscope, opret)
                    {
                        int j = _codes.Count - 3;
                        _codes[j] = new Code(Opcode.Load, v);
                        _codes.RemoveRange(j + 1, _codes.Count - (j + 1));
                        ScopeInfo s = _scopes[_scopes.Count - 1];
                        s.Funcs.RemoveAt(s.Funcs.Count - 1);
                        break;
                    }
                    case 3: // optimize one instruction argument (opscope, opX, opret)
                    {
                        int j = _codes.Count - 4;
                        if (_codes[j + 2]!.Op == Opcode.Const)
                        {
                            _codes[j] = new Code(Opcode.Push, _codes[j + 2]!.V);
                            _codes.RemoveRange(j + 1, _codes.Count - (j + 1));
                        }
                        else
                        {
                            _codes[j] = new Code(Opcode.Load, v);
                            _codes[j + 1] = _codes[j + 2];
                            _codes.RemoveRange(j + 2, _codes.Count - (j + 2));
                        }
                        ScopeInfo s = _scopes[_scopes.Count - 1];
                        s.Funcs.RemoveAt(s.Funcs.Count - 1);
                        break;
                    }
                    default:
                        Append(new Code(Opcode.Load, v));
                        Append(new Code(Opcode.PushPc, pc));
                        Append(new Code(Opcode.CallPc));
                        break;
                }
            }
            else
            {
                Append(new Code(Opcode.PushPc, pc));
            }
            if (i == indexing)
            {
                if (_codes[_codes.Count - 2]!.Op == Opcode.ExpBegin)
                {
                    _codes[_codes.Count - 2] = _codes[_codes.Count - 1];
                    _codes.RemoveAt(_codes.Count - 1);
                }
                else
                {
                    Append(new Code(Opcode.ExpEnd));
                }
            }
        }
        if (indexing > 0)
        {
            Append(new Code(Opcode.Push, null));
        }
        else
        {
            Append(new Code(Opcode.Load, v));
        }
        Append(new Code(Opcode.Call, fn));
    }

    private void Append(Code code)
    {
        _codes.Add(code);
    }

    private void Appends(params Code[] codes)
    {
        _codes.AddRange(codes);
    }

    private Action Lazy(Func<Code> f)
    {
        int i = _codes.Count;
        _codes.Add(null);
        return () => _codes[i] = f();
    }

    private void OptimizeTailRec()
    {
        List<int> pcs = new();
        Dictionary<int, bool> scopes = new();
        for (int i = 0; i < _codes.Count; i++)
        {
            Code code = _codes[i]!;
            switch (code.Op)
            {
                case Opcode.Scope:
                {
                    pcs.Add(i);
                    ScopeRef v = (ScopeRef)code.V!;
                    if (v.Args == 0)
                    {
                        scopes[i] = v.Variables == 0;
                    }
                    break;
                }
                case Opcode.Call:
                {
                    if (code.V is not int j || pcs.Count == 0 || pcs[pcs.Count - 1] != j)
                    {
                        break;
                    }
                    if (!scopes.TryGetValue(j, out bool canjump))
                    {
                        break;
                    }
                    for (int k = i + 1; k < _codes.Count;)
                    {
                        Code next = _codes[k]!;
                        if (next.Op == Opcode.Jump)
                        {
                            k = (int)next.V!;
                            continue;
                        }
                        if (next.Op == Opcode.Ret)
                        {
                            if (canjump)
                            {
                                code.Op = Opcode.Jump;
                                code.V = pcs[pcs.Count - 1] + 1;
                            }
                            else
                            {
                                code.Op = Opcode.CallRec;
                            }
                        }
                        break;
                    }
                    break;
                }
                case Opcode.Ret:
                    if (pcs.Count == 0)
                    {
                        return;
                    }
                    pcs.RemoveAt(pcs.Count - 1);
                    break;
            }
        }
    }

    private void OptimizeCodeOps()
    {
        Code? next = null;
        for (int i = _codes.Count - 1; i >= 0; i--)
        {
            Code code = _codes[i]!;
            switch (code.Op)
            {
                case Opcode.Push:
                case Opcode.Dup:
                case Opcode.Load:
                    if (next == null)
                    {
                        break;
                    }
                    switch (next.Op)
                    {
                        case Opcode.Pop:
                            code.Op = Opcode.Nop;
                            next.Op = Opcode.Nop;
                            break;
                        case Opcode.Const:
                            code.Op = Opcode.Nop;
                            next.Op = Opcode.Push;
                            break;
                    }
                    break;
                case Opcode.Jump:
                case Opcode.JumpIfNot:
                {
                    int j = (int)code.V!;
                    if (j - 1 == i)
                    {
                        code.Op = Opcode.Nop;
                    }
                    else if ((next = _codes[j]) != null && next.Op == Opcode.Jump)
                    {
                        code.V = next.V;
                    }
                    break;
                }
            }
            next = code;
        }
    }
}
