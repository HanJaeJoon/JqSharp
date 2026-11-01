namespace JqSharp;

/// <summary>
/// Loads the modules referred to by <c>import</c> and <c>include</c> directives.
/// </summary>
public interface IJqModuleLoader
{
    /// <summary>
    /// Returns the modules whose definitions are available to every query, like <c>~/.jq</c>.
    /// </summary>
    IEnumerable<JqQuery> LoadInitModules();

    /// <summary>
    /// Loads a jq module.
    /// </summary>
    /// <param name="name">The module path given in the directive.</param>
    /// <param name="meta">The metadata object of the directive, or <see langword="null"/>.</param>
    JqQuery LoadModule(string name, Dictionary<string, object?>? meta);

    /// <summary>
    /// Loads a JSON data module (<c>import "path" as $name</c>); gojq binds the array of the values in the file.
    /// </summary>
    /// <param name="name">The module path given in the directive.</param>
    /// <param name="meta">The metadata object of the directive, or <see langword="null"/>.</param>
    object? LoadJson(string name, Dictionary<string, object?>? meta);
}

/// <summary>
/// Loads modules from the file system, searching the given directories.
/// </summary>
/// <remarks>
/// Modules can extend the search with a <c>"search"</c> path in their metadata, which may lead outside the directories.
/// </remarks>
public sealed class JqModuleLoader : IJqModuleLoader
{
    private readonly List<string> _paths = new();

    /// <summary>
    /// Creates a loader searching the directories in order. Empty paths are ignored; use <c>"."</c> for the current directory.
    /// A path named <c>.jq</c> that is a file is loaded as an init module.
    /// </summary>
    /// <param name="paths">The directories; <c>~/</c> and <c>$ORIGIN/</c> prefixes are expanded.</param>
    public JqModuleLoader(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            string p = ResolvePath(path, "");
            if (p != "")
            {
                _paths.Add(p);
            }
        }
    }

    /// <inheritdoc/>
    public IEnumerable<JqQuery> LoadInitModules()
    {
        List<JqQuery> qs = new();
        foreach (string path in _paths)
        {
            if (Path.GetFileName(path) != ".jq" || !File.Exists(path))
            {
                continue;
            }
            string cnt = File.ReadAllText(path);
            qs.Add(ParseModule(path, cnt, Path.GetDirectoryName(path) ?? ""));
        }
        return qs;
    }

    /// <inheritdoc/>
    public JqQuery LoadModule(string name, Dictionary<string, object?>? meta)
    {
        string path = LookupModule(name, ".jq", meta);
        string cnt = File.ReadAllText(path);
        return ParseModule(path, cnt, Path.GetDirectoryName(path) ?? "");
    }

    /// <inheritdoc/>
    public object? LoadJson(string name, Dictionary<string, object?>? meta)
    {
        string path = LookupModule(name, ".json", meta);
        try
        {
            return Decoder.DecodeAll(File.ReadAllText(path));
        }
        catch (JqException err)
        {
            throw new JsonParseError(path, err);
        }
    }

    private string LookupModule(string name, string extension, Dictionary<string, object?>? meta)
    {
        List<string> paths = _paths;
        if (meta != null && meta.TryGetValue("search", out object? search) && search is string s)
        {
            string p = ResolvePath(s, "");
            if (p != "")
            {
                paths = new List<string> { p };
                paths.AddRange(_paths);
            }
        }
        foreach (string dir in paths)
        {
            string path = Path.Combine(dir, name + extension);
            if (File.Exists(path))
            {
                return path;
            }
            path = Path.Combine(dir, name, Path.GetFileName(name) + extension);
            if (File.Exists(path))
            {
                return path;
            }
        }
        throw new SimpleError("module not found: " + Encoder.Marshal(name));
    }

    private static JqQuery ParseModule(string path, string cnt, string dir)
    {
        JqQuery q;
        try
        {
            q = JqQuery.Parse(cnt);
        }
        catch (JqParseException err)
        {
            throw new QueryParseError(path, err);
        }
        if (q.Imports != null)
        {
            foreach (Import i in q.Imports)
            {
                if (i.Meta?.KeyVals == null)
                {
                    continue;
                }
                foreach (ConstObjectKeyVal e in i.Meta.KeyVals)
                {
                    if ((e.Key == "search" || e.KeyString == "search") && e.Val.TryToString(out string p))
                    {
                        p = ResolvePath(p, dir);
                        if (p != "")
                        {
                            e.Val.Str = p;
                        }
                        else
                        {
                            e.Val.Null = true;
                        }
                    }
                }
            }
        }
        return q;
    }

    private static string ResolvePath(string path, string dir)
    {
        if (path == "")
        {
            return "";
        }
        if (path.StartsWith("~/", StringComparison.Ordinal))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return home == "" ? "" : Path.Combine(home, path.Substring(2));
        }
        if (path.StartsWith("$ORIGIN/", StringComparison.Ordinal))
        {
            return Path.Combine(AppContext.BaseDirectory, path.Substring(8));
        }
        if (Path.IsPathRooted(path))
        {
            return path;
        }
        return dir == "" ? path : Path.Combine(dir, path);
    }
}
