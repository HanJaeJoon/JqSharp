namespace JqSharp;

// The builtins defined in jq (builtin.jq), parsed on first use and grouped by name.
internal static class Builtin
{
    private static readonly Lazy<Dictionary<string, List<FuncDef>>> Defs = new(Load);

    public static Dictionary<string, List<FuncDef>> FuncDefs => Defs.Value;

    private static Dictionary<string, List<FuncDef>> Load()
    {
        string src;
        using (Stream stream = typeof(Builtin).Assembly.GetManifestResourceStream("JqSharp.builtin.jq")!)
        using (StreamReader reader = new(stream))
        {
            src = reader.ReadToEnd();
        }
        Dictionary<string, List<FuncDef>> fds = new();
        foreach (FuncDef fd in JqQuery.Parse(src).FuncDefs!)
        {
            if (!fds.TryGetValue(fd.Name, out List<FuncDef>? list))
            {
                fds[fd.Name] = list = new List<FuncDef>();
            }
            list.Add(fd);
        }
        // compiled by hand in the compiler
        fds["_assign"] = new List<FuncDef>();
        fds["_modify"] = new List<FuncDef>();
        fds["_last"] = new List<FuncDef>();
        return fds;
    }
}
