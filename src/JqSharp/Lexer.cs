using System.Text;

namespace JqSharp;

internal sealed class Lexer
{
    private const int Eof = -1;

    private const int NumberStateLead = 0;
    private const int NumberStateFloat = 1;
    private const int NumberStateExpLead = 2;
    private const int NumberStateExp = 3;

    private static readonly Dictionary<string, int> Keywords = new()
    {
        ["or"] = Parser.tokOrOp,
        ["and"] = Parser.tokAndOp,
        ["module"] = Parser.tokModule,
        ["import"] = Parser.tokImport,
        ["include"] = Parser.tokInclude,
        ["def"] = Parser.tokDef,
        ["as"] = Parser.tokAs,
        ["label"] = Parser.tokLabel,
        ["break"] = Parser.tokBreak,
        ["null"] = Parser.tokNull,
        ["true"] = Parser.tokTrue,
        ["false"] = Parser.tokFalse,
        ["if"] = Parser.tokIf,
        ["then"] = Parser.tokThen,
        ["elif"] = Parser.tokElif,
        ["else"] = Parser.tokElse,
        ["end"] = Parser.tokEnd,
        ["try"] = Parser.tokTry,
        ["catch"] = Parser.tokCatch,
        ["reduce"] = Parser.tokReduce,
        ["foreach"] = Parser.tokForeach,
    };

    private readonly string _source;
    private int _offset;
    private string _token = "";
    private int _tokenType;

    public Lexer(string source)
    {
        _source = source;
    }

    public JqQuery? Result { get; set; }

    public bool InString { get; set; }

    public JqParseException? Err { get; private set; }

    public int Lex(ref SymType lval)
    {
        int tokenType = LexInternal(ref lval);
        _tokenType = tokenType;
        return tokenType;
    }

    private int LexInternal(ref SymType lval)
    {
        if (_source.Length == _offset)
        {
            _token = "";
            return Eof;
        }
        if (InString)
        {
            int tok = ScanString(_offset, out string str);
            lval.Token = str;
            return tok;
        }
        if (!Next(out char ch))
        {
            _token = "";
            return Eof;
        }
        if (IsIdent(ch, false))
        {
            int i = _offset - 1;
            int j = ScanIdentOrModule(out bool isModule);
            _token = _source.Substring(i, j - i);
            lval.Token = _token;
            if (isModule)
            {
                return Parser.tokModuleIdent;
            }
            if (Keywords.TryGetValue(_token, out int keyword))
            {
                return keyword;
            }
            return Parser.tokIdent;
        }
        if (IsNumber(ch))
        {
            int i = _offset - 1;
            int j = ScanNumber(NumberStateLead);
            if (j < 0)
            {
                _token = _source.Substring(i, -j - i);
                return Parser.tokInvalid;
            }
            _token = _source.Substring(i, j - i);
            lval.Token = _token;
            return Parser.tokNumber;
        }
        switch (ch)
        {
            case '.':
            {
                char c = Peek();
                if (c == '.')
                {
                    _offset++;
                    _token = "..";
                    return Parser.tokRecurse;
                }
                if (IsIdent(c, false))
                {
                    int i = _offset - 1;
                    _token = _source.Substring(i, ScanIdent() - i);
                    lval.Token = _token.Substring(1);
                    return Parser.tokIndex;
                }
                if (IsNumber(c))
                {
                    int i = _offset - 1;
                    int j = ScanNumber(NumberStateFloat);
                    if (j < 0)
                    {
                        _token = _source.Substring(i, -j - i);
                        return Parser.tokInvalid;
                    }
                    _token = _source.Substring(i, j - i);
                    lval.Token = _token;
                    return Parser.tokNumber;
                }
                return '.';
            }
            case '$':
                if (IsIdent(Peek(), false))
                {
                    int i = _offset - 1;
                    int j = ScanIdentOrModule(out bool isModule);
                    _token = _source.Substring(i, j - i);
                    lval.Token = _token;
                    return isModule ? Parser.tokModuleVariable : Parser.tokVariable;
                }
                break;
            case '|':
                if (Peek() == '=')
                {
                    return UpdateOp(ref lval, "|=", Operator.Modify);
                }
                break;
            case '+':
                if (Peek() == '=')
                {
                    return UpdateOp(ref lval, "+=", Operator.UpdateAdd);
                }
                break;
            case '-':
                if (Peek() == '=')
                {
                    return UpdateOp(ref lval, "-=", Operator.UpdateSub);
                }
                break;
            case '*':
                if (Peek() == '=')
                {
                    return UpdateOp(ref lval, "*=", Operator.UpdateMul);
                }
                break;
            case '/':
                switch (Peek())
                {
                    case '=':
                        return UpdateOp(ref lval, "/=", Operator.UpdateDiv);
                    case '/':
                        _offset++;
                        if (Peek() == '=')
                        {
                            return UpdateOp(ref lval, "//=", Operator.UpdateAlt);
                        }
                        _token = "//";
                        lval.Operator = Operator.Alt;
                        return Parser.tokAltOp;
                }
                break;
            case '%':
                if (Peek() == '=')
                {
                    return UpdateOp(ref lval, "%=", Operator.UpdateMod);
                }
                break;
            case '=':
                if (Peek() == '=')
                {
                    _offset++;
                    _token = "==";
                    lval.Operator = Operator.Eq;
                    return Parser.tokCompareOp;
                }
                _token = "=";
                lval.Operator = Operator.Assign;
                return Parser.tokUpdateOp;
            case '!':
                if (Peek() == '=')
                {
                    _offset++;
                    _token = "!=";
                    lval.Operator = Operator.Ne;
                    return Parser.tokCompareOp;
                }
                break;
            case '>':
                if (Peek() == '=')
                {
                    _offset++;
                    _token = ">=";
                    lval.Operator = Operator.Ge;
                    return Parser.tokCompareOp;
                }
                _token = ">";
                lval.Operator = Operator.Gt;
                return Parser.tokCompareOp;
            case '<':
                if (Peek() == '=')
                {
                    _offset++;
                    _token = "<=";
                    lval.Operator = Operator.Le;
                    return Parser.tokCompareOp;
                }
                _token = "<";
                lval.Operator = Operator.Lt;
                return Parser.tokCompareOp;
            case '@':
                if (IsIdent(Peek(), true))
                {
                    int i = _offset - 1;
                    _token = _source.Substring(i, ScanIdent() - i);
                    lval.Token = _token;
                    return Parser.tokFormat;
                }
                break;
            case '"':
            {
                int tok = ScanString(_offset - 1, out string str);
                lval.Token = str;
                return tok;
            }
            default:
                if (ch >= 0x80)
                {
                    int size = char.IsHighSurrogate(ch) && _offset < _source.Length && char.IsLowSurrogate(_source[_offset]) ? 2 : 1;
                    _token = _source.Substring(_offset - 1, size);
                    _offset += size - 1;
                    // gojq returns the first UTF-8 byte, which never matches a grammar token
                    return 0x80;
                }
                break;
        }
        return ch;
    }

    private int UpdateOp(ref SymType lval, string token, Operator op)
    {
        _offset += token.Length - 1;
        _token = token;
        lval.Operator = op;
        return Parser.tokUpdateOp;
    }

    private bool Next(out char ch)
    {
        while (true)
        {
            ch = _source[_offset];
            _offset++;
            if (ch == '#')
            {
                if (SkipComment())
                {
                    return false;
                }
            }
            else if (!IsWhite(ch))
            {
                return true;
            }
            else if (_source.Length == _offset)
            {
                return false;
            }
        }
    }

    private bool SkipComment()
    {
        while (true)
        {
            switch (Peek())
            {
                case '\0':
                    return true;
                case '\\':
                    _offset++;
                    switch (Peek())
                    {
                        case '\\':
                        case '\n':
                            _offset++;
                            break;
                        case '\r':
                            _offset++;
                            if (Peek() == '\n')
                            {
                                _offset++;
                            }
                            break;
                    }
                    break;
                case '\n':
                case '\r':
                    return false;
                default:
                    _offset++;
                    break;
            }
        }
    }

    private char Peek()
    {
        return _source.Length == _offset ? '\0' : _source[_offset];
    }

    private int ScanIdent()
    {
        while (IsIdent(Peek(), true))
        {
            _offset++;
        }
        return _offset;
    }

    private int ScanIdentOrModule(out bool isModule)
    {
        int index = ScanIdent();
        isModule = false;
        if (Peek() == ':')
        {
            _offset++;
            if (Peek() == ':')
            {
                _offset++;
                if (IsIdent(Peek(), false))
                {
                    _offset++;
                    index = ScanIdent();
                    isModule = true;
                }
                else
                {
                    _offset -= 2;
                }
            }
            else
            {
                _offset--;
            }
        }
        return index;
    }

    public bool ValidVarName()
    {
        if (Peek() != '$')
        {
            return false;
        }
        _offset++;
        return IsIdent(Peek(), false) && ScanIdent() == _source.Length;
    }

    private int ScanNumber(int state)
    {
        while (true)
        {
            switch (state)
            {
                case NumberStateLead:
                case NumberStateFloat:
                {
                    char ch = Peek();
                    if (IsNumber(ch))
                    {
                        _offset++;
                        break;
                    }
                    switch (ch)
                    {
                        case '.':
                            if (state != NumberStateLead)
                            {
                                _offset++;
                                return -_offset;
                            }
                            _offset++;
                            state = NumberStateFloat;
                            break;
                        case 'e':
                        case 'E':
                            _offset++;
                            switch (Peek())
                            {
                                case '-':
                                case '+':
                                    _offset++;
                                    break;
                            }
                            state = NumberStateExpLead;
                            break;
                        default:
                            if (IsIdent(ch, false))
                            {
                                _offset++;
                                return -_offset;
                            }
                            return _offset;
                    }
                    break;
                }
                default:
                {
                    char ch = Peek();
                    if (!IsNumber(ch))
                    {
                        if (IsIdent(ch, false))
                        {
                            _offset++;
                            return -_offset;
                        }
                        if (state == NumberStateExpLead)
                        {
                            return -_offset;
                        }
                        return _offset;
                    }
                    _offset++;
                    state = NumberStateExp;
                    break;
                }
            }
        }
    }

    public bool ValidNumber()
    {
        char ch = Peek();
        switch (ch)
        {
            case '+':
            case '-':
                _offset++;
                ch = Peek();
                break;
        }
        int state = NumberStateLead;
        if (ch == '.')
        {
            _offset++;
            ch = Peek();
            state = NumberStateFloat;
        }
        return IsNumber(ch) && ScanNumber(state) == _source.Length;
    }

    private int ScanString(int start, out string str)
    {
        str = "";
        bool decode = false;
        for (int i = _offset; i < _source.Length; i++)
        {
            char ch = _source[i];
            switch (ch)
            {
                case '\\':
                    if (++i >= _source.Length)
                    {
                        break;
                    }
                    switch (_source[i])
                    {
                        case 'u':
                            for (int j = 1; j <= 4; j++)
                            {
                                if (i + j >= _source.Length || !IsHex(_source[i + j]))
                                {
                                    _offset = i + j;
                                    _token = _source.Substring(i - 1, _offset - (i - 1));
                                    return Parser.tokInvalidEscapeSequence;
                                }
                            }
                            i += 4;
                            decode = true;
                            break;
                        case '"':
                        case '/':
                        case '\\':
                        case 'b':
                        case 'f':
                        case 'n':
                        case 'r':
                        case 't':
                            decode = true;
                            break;
                        case '(':
                            if (!InString)
                            {
                                InString = true;
                                return Parser.tokStringStart;
                            }
                            if (i == _offset + 1)
                            {
                                _offset += 2;
                                InString = false;
                                return Parser.tokStringQuery;
                            }
                            _offset = i - 1;
                            _token = _source.Substring(start, _offset - start);
                            str = Unquote(_token, true, decode);
                            return Parser.tokString;
                        default:
                            _offset = i + 1;
                            _token = _source.Substring(_offset - 2, 2);
                            return Parser.tokInvalidEscapeSequence;
                    }
                    break;
                case '"':
                    if (!InString)
                    {
                        _offset = i + 1;
                        _token = _source.Substring(start, _offset - start);
                        str = Unquote(_token, false, decode);
                        return Parser.tokString;
                    }
                    if (i > _offset)
                    {
                        _offset = i;
                        _token = _source.Substring(start, _offset - start);
                        str = Unquote(_token, true, decode);
                        return Parser.tokString;
                    }
                    InString = false;
                    _offset = i + 1;
                    return Parser.tokStringEnd;
                default:
                    if (char.IsSurrogate(ch))
                    {
                        decode = true;
                    }
                    break;
            }
        }
        _offset = _source.Length;
        _token = "";
        return Parser.tokUnterminatedString;
    }

    // The escapes were validated by ScanString, so this only decodes them like encoding/json does.
    private static string Unquote(string src, bool quote, bool decode)
    {
        string s = quote ? src : src.Substring(1, src.Length - 2);
        if (!decode)
        {
            return s;
        }
        StringBuilder sb = new(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            if (ch != '\\')
            {
                if (char.IsHighSurrogate(ch) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    sb.Append(ch).Append(s[++i]);
                }
                else
                {
                    sb.Append(char.IsSurrogate(ch) ? '\uFFFD' : ch);
                }
                continue;
            }
            ch = s[++i];
            switch (ch)
            {
                case 'b':
                    sb.Append('\b');
                    break;
                case 'f':
                    sb.Append('\f');
                    break;
                case 'n':
                    sb.Append('\n');
                    break;
                case 'r':
                    sb.Append('\r');
                    break;
                case 't':
                    sb.Append('\t');
                    break;
                case 'u':
                {
                    TryParseHex4(s, i + 1, out int r);
                    i += 4;
                    if (char.IsSurrogate((char)r))
                    {
                        if (char.IsHighSurrogate((char)r) && i + 2 < s.Length && s[i + 1] == '\\' && s[i + 2] == 'u'
                            && TryParseHex4(s, i + 3, out int r2) && char.IsLowSurrogate((char)r2))
                        {
                            sb.Append((char)r).Append((char)r2);
                            i += 6;
                        }
                        else
                        {
                            sb.Append('\uFFFD');
                        }
                    }
                    else
                    {
                        sb.Append((char)r);
                    }
                    break;
                }
                default:
                    sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }

    internal static bool TryParseHex4(string s, int i, out int r)
    {
        r = 0;
        if (i + 4 > s.Length)
        {
            return false;
        }
        for (int j = 0; j < 4; j++)
        {
            char c = s[i + j];
            int d = c >= '0' && c <= '9' ? c - '0'
                : c >= 'a' && c <= 'f' ? c - 'a' + 10
                : c >= 'A' && c <= 'F' ? c - 'A' + 10
                : -1;
            if (d < 0)
            {
                return false;
            }
            r = (r << 4) | d;
        }
        return true;
    }

    public void Error()
    {
        int offset = _offset;
        string token = _token;
        if (_tokenType != Eof && _tokenType < 0x80)
        {
            token = ((char)_tokenType).ToString();
        }
        int byteOffset = Encoding.UTF8.GetByteCount(_source.Substring(0, Math.Min(offset, _source.Length)));
        Err = new JqParseException(byteOffset, token, _tokenType switch
        {
            Eof => "unexpected EOF",
            Parser.tokInvalid => "invalid token " + Encoder.Marshal(token),
            Parser.tokInvalidEscapeSequence => "invalid escape sequence \"" + token + "\" in string literal",
            Parser.tokUnterminatedString => "unterminated string literal",
            _ => "unexpected token " + Encoder.Marshal(token),
        });
    }

    private static bool IsWhite(char ch)
    {
        return ch == '\t' || ch == '\n' || ch == '\r' || ch == ' ';
    }

    private static bool IsIdent(char ch, bool tail)
    {
        return (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || ch == '_' || (tail && IsNumber(ch));
    }

    private static bool IsHex(char ch)
    {
        return (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F') || IsNumber(ch);
    }

    private static bool IsNumber(char ch)
    {
        return ch >= '0' && ch <= '9';
    }
}
