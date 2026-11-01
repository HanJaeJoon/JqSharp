// Ported from gojq parser.go, which goyacc generates from parser.go.y.
// The tables below are copied verbatim; the semantic actions follow parser.go.y.

namespace JqSharp;

internal struct SymType
{
    public int Yys;
    public object? Value;
    public string Token;
    public Operator Operator;
}

internal static class Parser
{
    private static readonly short[] yyExca =
    {
        -1, 1, 1, -1, -2, 0, -1, 139, 5, 0, -2, 27, -1, 142, 6, 0,
        -2, 30, -1, 194, 58, 114, -2, 49,
    };

    private static readonly short[] yyAct =
    {
        79, 129, 174, 195, 103, 10, 210, 190, 175, 128, 135, 32, 5, 93, 102, 108,
        48, 179, 180, 181, 130, 74, 50, 6, 227, 74, 158, 74, 110, 15, 98, 246,
        97, 99, 115, 177, 123, 178, 104, 120, 226, 186, 157, 114, 74, 245, 243, 131,
        74, 179, 180, 181, 122, 132, 73, 75, 265, 182, 73, 75, 73, 75, 219, 6,
        116, 117, 118, 177, 282, 178, 204, 225, 253, 203, 127, 256, 133, 73, 75, 254,
        234, 73, 75, 233, 207, 224, 136, 206, 280, 182, 240, 279, 229, 74, 186, 6,
        237, 153, 74, 242, 156, 74, 148, 149, 228, 160, 161, 151, 159, 150, 126, 138,
        139, 140, 141, 142, 143, 144, 145, 146, 147, 125, 124, 198, 74, 45, 73, 75,
        183, 184, 239, 73, 75, 188, 73, 75, 137, 191, 199, 50, 173, 162, 92, 185,
        192, 278, 74, 119, 252, 200, 74, 186, 77, 202, 74, 205, 76, 73, 75, 43,
        44, 17, 95, 94, 214, 96, 74, 212, 80, 74, 104, 244, 218, 209, 196, 197,
        216, 217, 91, 73, 75, 43, 44, 73, 75, 221, 222, 73, 75, 211, 211, 112,
        113, 213, 230, 215, 165, 168, 273, 73, 75, 96, 73, 75, 136, 236, 231, 74,
        238, 267, 271, 74, 81, 82, 85, 83, 84, 260, 261, 74, 9, 167, 88, 89,
        90, 247, 170, 248, 171, 169, 250, 251, 191, 134, 78, 249, 3, 223, 255, 192,
        73, 75, 25, 262, 263, 24, 257, 258, 220, 264, 176, 270, 73, 75, 47, 266,
        86, 87, 88, 89, 90, 268, 269, 232, 85, 83, 84, 272, 101, 211, 211, 275,
        276, 85, 164, 277, 81, 82, 85, 83, 84, 281, 52, 53, 54, 55, 56, 57,
        58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 106,
        107, 259, 86, 87, 88, 89, 90, 43, 44, 241, 155, 86, 87, 88, 89, 90,
        86, 87, 88, 89, 90, 16, 121, 14, 37, 21, 22, 23, 33, 189, 105, 187,
        201, 34, 208, 35, 36, 39, 41, 40, 42, 19, 20, 28, 31, 43, 44, 7,
        8, 4, 2, 81, 82, 85, 83, 84, 1, 29, 30, 0, 166, 85, 18, 84,
        0, 27, 0, 154, 38, 0, 152, 26, 52, 53, 54, 55, 56, 57, 58, 59,
        60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 106, 107, 86,
        87, 88, 89, 90, 0, 43, 44, 86, 87, 88, 89, 90, 86, 87, 88, 89,
        90, 11, 12, 16, 0, 14, 37, 21, 22, 23, 33, 0, 105, 0, 0, 34,
        100, 35, 36, 39, 41, 40, 42, 19, 20, 28, 31, 43, 44, 74, 0, 0,
        111, 0, 0, 0, 274, 13, 0, 29, 30, 0, 0, 0, 18, 0, 13, 27,
        0, 0, 38, 0, 16, 26, 14, 37, 21, 22, 23, 33, 0, 0, 73, 75,
        34, 0, 35, 36, 39, 41, 40, 42, 19, 20, 28, 31, 43, 44, 74, 0,
        0, 0, 0, 0, 0, 163, 0, 0, 29, 30, 0, 0, 0, 18, 0, 0,
        27, 0, 0, 38, 0, 235, 26, 16, 0, 14, 37, 21, 22, 23, 33, 73,
        75, 0, 0, 34, 0, 35, 36, 39, 41, 40, 42, 19, 20, 28, 31, 43,
        44, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 29, 30, 0, 0, 0,
        18, 0, 0, 27, 0, 0, 38, 0, 109, 26, 16, 0, 14, 37, 21, 22,
        23, 33, 0, 0, 0, 0, 34, 0, 35, 36, 39, 41, 40, 42, 19, 20,
        28, 31, 43, 44, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 29, 30,
        0, 0, 0, 18, 0, 0, 27, 0, 0, 38, 0, 0, 26, 52, 53, 54,
        55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70,
        71, 72, 49, 0, 0, 0, 0, 0, 0, 0, 51, 52, 53, 54, 55, 56,
        57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72,
        49, 0, 0, 0, 0, 172, 0, 0, 51, 37, 21, 22, 23, 33, 0, 0,
        0, 0, 34, 0, 35, 36, 39, 41, 40, 42, 19, 20, 28, 31, 43, 44,
        0, 0, 0, 46, 0, 0, 0, 0, 0, 0, 29, 30, 0, 0, 0, 18,
        0, 0, 27, 0, 0, 38, 0, 0, 26, 52, 53, 54, 55, 56, 57, 58,
        59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 106, 194,
        0, 0, 0, 0, 0, 0, 43, 44, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 193,
    };

    private static readonly short[] yyPact =
    {
        227, -32768, -32768, -39, 407, 68, 644, -32768, -32768, -32768, 156, 120, 116, 558, 139, 272,
        150, 109, 145, -32768, -32768, -32768, -32768, -32768, -26, -32768, 369, 507, -32768, 666, 666, 123,
        -32768, 558, 666, 666, 666, 118, 558, -32768, -32768, -32768, -32768, -32768, -32768, -32768, -32768, -11,
        -32768, 64, 63, 52, -32768, -32768, -32768, -32768, -32768, -32768, -32768, -32768, -32768, -32768, -32768, -32768,
        -32768, -32768, -32768, -32768, -32768, -32768, -32768, -32768, -32768, 558, -9, 558, 220, -39, -32768, 156,
        90, 666, 666, 666, 666, 666, 666, 666, 666, 666, 666, 44, -32768, -32768, -32768, 145,
        313, -32768, -32768, 558, -32768, -21, -32768, 50, 47, 558, -32768, -32768, -32768, -32768, 80, 558,
        109, 109, -32768, 481, 171, 351, 208, -32768, 137, 190, -32768, 614, 33, 33, 33, 156,
        97, -32768, -32768, -9, 722, 198, 146, 66, -32768, 558, 272, 258, 359, 267, 364, 172,
        172, -32768, -32768, -32768, 558, 146, -32768, -32768, -32768, 12, 558, 27, 156, -32768, 275, 666,
        666, 133, -32768, 558, -32768, 666, -9, -9, -32768, -32768, -32768, 558, -32768, -32768, -32768, -32768,
        -32768, -32768, -32768, -32768, -32768, -32768, 1, -32768, -32768, 558, 233, 24, -32768, -23, -32768, 46,
        34, 558, -32768, -39, -32768, -32768, -32768, 156, 206, 23, -32768, -32768, 456, 35, -32768, 558,
        -32768, -32768, 84, 272, 84, 32, 156, -32768, 40, -13, 111, -32768, -16, -32768, 156, -9,
        -32768, -9, -32768, 722, -9, -9, 88, 15, -32768, 21, 146, -32768, 14, -32768, 156, 666,
        666, 196, 558, 558, -32768, -32768, 33, -32768, -32768, -32768, -32768, -32768, -2, -32768, 558, -32768,
        -32768, 84, 84, 186, 558, 558, 194, 153, -32768, -9, 141, -32768, 432, 156, 558, 558,
        -32768, -32768, 558, 85, 31, 156, -32768, -32768, 558, 8, -32768,
    };

    private static readonly short[] yyPgo =
    {
        0, 360, 354, 353, 352, 10, 351, 220, 448, 336, 0, 9, 1, 335, 333, 7,
        29, 161, 11, 326, 13, 314, 313, 305, 274, 268, 14, 6, 2, 8, 254, 16,
        250, 248, 3, 245, 242, 15, 4,
    };

    private static readonly sbyte[] yyR1 =
    {
        0, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8,
        9, 9, 34, 34, 10, 10, 10, 10, 10, 10, 16, 16, 16, 16, 16, 16,
        16, 16, 16, 16, 16, 11, 11, 12, 12, 12, 13, 13, 14, 14, 15, 15,
        15, 15, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17,
        17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17,
        17, 17, 17, 17, 18, 18, 19, 19, 19, 35, 35, 36, 36, 20, 20, 20,
        20, 20, 21, 21, 22, 22, 23, 23, 24, 24, 25, 25, 26, 26, 26, 26,
        26, 38, 38, 38, 27, 27, 28, 28, 28, 28, 28, 28, 28, 29, 29, 29,
        30, 30, 31, 31, 31, 32, 32, 33, 33, 37, 37, 37, 37, 37, 37, 37,
        37, 37, 37, 37, 37, 37, 37, 37, 37, 37, 37, 37, 37, 37,
    };

    private static readonly sbyte[] yyR2 =
    {
        0, 3, 0, 3, 0, 2, 6, 4, 0, 1, 1, 1, 0, 2, 5, 8,
        1, 3, 1, 1, 2, 3, 5, 4, 3, 1, 3, 3, 3, 3, 3, 3,
        3, 3, 3, 3, 1, 1, 4, 1, 3, 3, 1, 3, 1, 3, 3, 3,
        5, 1, 1, 1, 1, 2, 2, 1, 1, 1, 1, 4, 1, 2, 3, 4,
        2, 3, 1, 2, 2, 1, 2, 1, 7, 3, 9, 9, 11, 2, 3, 2,
        2, 2, 3, 3, 1, 3, 0, 2, 4, 1, 1, 1, 1, 2, 3, 4,
        4, 5, 1, 3, 0, 5, 0, 2, 0, 2, 1, 3, 3, 3, 5, 1,
        1, 1, 1, 1, 3, 1, 1, 1, 1, 1, 1, 1, 1, 2, 3, 4,
        1, 3, 3, 3, 3, 2, 3, 1, 3, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
    };

    private static readonly short[] yyChk =
    {
        -32768, -1, -2, 9, -3, -29, 62, -6, -4, -7, -10, 10, 11, -8, 14, -16,
        12, -17, 53, 32, 33, 16, 17, 18, -35, -36, 62, 56, 34, 48, 49, 35,
        -18, 19, 24, 26, 27, 15, 59, 28, 30, 29, 31, 36, 37, 57, 63, -30,
        -31, 28, -37, 36, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18,
        19, 20, 21, 22, 23, 24, 25, 26, 27, 46, 13, 47, 36, 36, -7, -10,
        29, 4, 5, 7, 8, 6, 48, 49, 50, 51, 52, 28, 33, -20, 54, 53,
        56, -20, -18, 59, 63, -25, -26, -38, -18, 59, 28, 29, -37, 61, -10, -8,
        -17, -17, -18, -10, -16, -16, -16, 29, -10, -19, 63, 47, 58, 58, 58, -10,
        -11, -12, 29, 56, 62, -10, 13, -5, -29, 46, -16, -16, -16, -16, -16, -16,
        -16, -16, -16, -16, 58, 59, -20, -18, 61, -10, 58, -21, -10, 63, 47, 58,
        58, -10, 61, 20, -24, 25, 13, 13, 60, 39, 36, 38, 63, -31, -28, -29,
        -32, 34, 36, 16, 17, 18, 56, -28, -28, 46, 54, -13, -12, -14, -15, -38,
        -18, 59, 29, -34, 28, 29, 57, -10, -10, -9, -34, 61, 58, -10, 60, 57,
        63, -26, -27, -16, -27, 60, -10, -16, -11, -11, -10, 61, -33, -28, -10, 4,
        61, 47, 63, 47, 58, 58, -10, -5, 57, 60, 57, 61, -10, 61, -10, 46,
        58, -22, 59, 59, 60, 61, 47, -12, -12, -15, -12, -12, 60, 57, 58, -34,
        61, -27, -27, -23, 21, 22, -10, -10, -28, 58, -10, 23, -10, -10, 57, 57,
        -12, 57, 20, -10, -10, -10, 60, 60, 57, -10, 60,
    };

    private static readonly short[] yyDef =
    {
        2, -2, 4, 0, 12, 0, 0, 1, 5, 10, 11, 0, 0, 12, 0, 25,
        0, 36, 50, 51, 52, 55, 56, 57, 58, 60, 0, 0, 66, 0, 0, 69,
        71, 0, 0, 0, 0, 0, 0, 89, 90, 91, 92, 84, 86, 3, 125, 0,
        128, 0, 0, 0, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148,
        149, 150, 151, 152, 153, 154, 155, 156, 157, 0, 0, 0, 0, 8, 13, 20,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 79, 80, 81, 0,
        0, 53, 54, 0, 61, 0, 106, 111, 112, 0, 113, 114, 115, 64, 0, 0,
        67, 68, 70, 0, 104, 0, 0, 77, 0, 0, 126, 0, 0, 0, 0, 21,
        0, 37, 39, 0, 0, 24, 0, 0, 9, 0, 26, -2, 28, 29, -2, 31,
        32, 33, 34, 35, 0, 0, 82, 83, 93, 0, 0, 0, 98, 62, 0, 0,
        0, 0, 65, 0, 73, 0, 0, 0, 78, 85, 87, 0, 127, 129, 130, 118,
        119, 120, 121, 122, 123, 124, 0, 131, 132, 0, 0, 0, 42, 0, 44, 0,
        0, 0, -2, 8, 18, 19, 7, 23, 0, 0, 16, 94, 0, 0, 59, 0,
        63, 107, 108, 117, 109, 0, 100, 105, 0, 0, 0, 133, 0, 135, 22, 0,
        40, 0, 41, 0, 0, 0, 0, 0, 14, 0, 0, 95, 0, 96, 99, 0,
        0, 102, 0, 0, 88, 134, 0, 38, 43, 45, 46, 47, 0, 6, 0, 17,
        97, 116, 110, 0, 0, 0, 0, 0, 136, 0, 0, 72, 0, 103, 0, 0,
        48, 15, 0, 0, 0, 101, 74, 75, 0, 0, 76,
    };

    private static readonly sbyte[] yyTok1 =
    {
        1, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
        3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
        3, 3, 3, 3, 3, 52, 3, 3, 59, 60, 50, 48, 47, 49, 53, 51,
        3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 58, 57, 3, 3, 3, 54,
        3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
        3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 56, 3, 61, 3, 3,
        3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
        3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 62, 46, 63,
    };

    private static readonly sbyte[] yyTok2 =
    {
        2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17,
        18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33,
        34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 55,
    };

    private static readonly sbyte[] yyTok3 =
    {
        0,
    };

    private const int yyLast = 782;

    private const int yyPrivate = 57344;

    internal const int tokAltOp = 57346;

    internal const int tokUpdateOp = 57347;

    internal const int tokCompareOp = 57348;

    internal const int tokOrOp = 57349;

    internal const int tokAndOp = 57350;

    internal const int tokModule = 57351;

    internal const int tokImport = 57352;

    internal const int tokInclude = 57353;

    internal const int tokDef = 57354;

    internal const int tokAs = 57355;

    internal const int tokLabel = 57356;

    internal const int tokBreak = 57357;

    internal const int tokNull = 57358;

    internal const int tokTrue = 57359;

    internal const int tokFalse = 57360;

    internal const int tokIf = 57361;

    internal const int tokThen = 57362;

    internal const int tokElif = 57363;

    internal const int tokElse = 57364;

    internal const int tokEnd = 57365;

    internal const int tokTry = 57366;

    internal const int tokCatch = 57367;

    internal const int tokReduce = 57368;

    internal const int tokForeach = 57369;

    internal const int tokIdent = 57370;

    internal const int tokVariable = 57371;

    internal const int tokModuleIdent = 57372;

    internal const int tokModuleVariable = 57373;

    internal const int tokRecurse = 57374;

    internal const int tokIndex = 57375;

    internal const int tokNumber = 57376;

    internal const int tokFormat = 57377;

    internal const int tokString = 57378;

    internal const int tokStringStart = 57379;

    internal const int tokStringQuery = 57380;

    internal const int tokStringEnd = 57381;

    internal const int tokInvalid = 57382;

    internal const int tokInvalidEscapeSequence = 57383;

    internal const int tokUnterminatedString = 57384;

    internal const int tokFuncDefQuery = 57385;

    internal const int tokExpr = 57386;

    internal const int tokTerm = 57387;

    internal const int tokEmptyCatch = 57388;


    private const int yyEofCode = 1;
    private const int yyErrCode = 2;
    private const int yyFlag = -32768;

    private static int Lex1(Lexer lex, ref SymType lval, out int token)
    {
        token = 0;
        int ch = lex.Lex(ref lval);
        if (ch <= 0)
        {
            token = yyTok1[0];
        }
        else if (ch < yyTok1.Length)
        {
            token = yyTok1[ch];
        }
        else if (ch >= yyPrivate && ch < yyPrivate + yyTok2.Length)
        {
            token = yyTok2[ch - yyPrivate];
        }
        else
        {
            for (int i = 0; i < yyTok3.Length; i += 2)
            {
                token = yyTok3[i + 0];
                if (token == ch)
                {
                    token = yyTok3[i + 1];
                    break;
                }
            }
        }
        if (token == 0)
        {
            token = yyTok2[1]; // unknown char
        }
        return ch;
    }

    // Returns 0 on success and 1 on a syntax error (reported through lex.Error).
    public static int Parse(Lexer yylex)
    {
        SymType lval = default;
        SymType yyVAL = default;
        SymType[] yyS = new SymType[16];
        int yyn;
        int errflag = 0;
        int yystate = 0;
        int yychar = -1;
        int yytoken = -1;
        int yyp = -1;

    yystack:
        yyp++;
        if (yyp >= yyS.Length)
        {
            Array.Resize(ref yyS, yyS.Length * 2);
        }
        yyS[yyp] = yyVAL;
        yyS[yyp].Yys = yystate;

    yynewstate:
        yyn = yyPact[yystate];
        if (yyn <= yyFlag)
        {
            goto yydefault; // simple state
        }
        if (yychar < 0)
        {
            yychar = Lex1(yylex, ref lval, out yytoken);
        }
        yyn += yytoken;
        if (yyn < 0 || yyn >= yyLast)
        {
            goto yydefault;
        }
        yyn = yyAct[yyn];
        if (yyChk[yyn] == yytoken)
        {
            // valid shift
            yychar = -1;
            yytoken = -1;
            yyVAL = lval;
            yystate = yyn;
            if (errflag > 0)
            {
                errflag--;
            }
            goto yystack;
        }

    yydefault:
        yyn = yyDef[yystate];
        if (yyn == -2)
        {
            if (yychar < 0)
            {
                yychar = Lex1(yylex, ref lval, out yytoken);
            }
            // look through exception table
            int xi = 0;
            while (!(yyExca[xi + 0] == -1 && yyExca[xi + 1] == yystate))
            {
                xi += 2;
            }
            for (xi += 2; ; xi += 2)
            {
                yyn = yyExca[xi + 0];
                if (yyn < 0 || yyn == yytoken)
                {
                    break;
                }
            }
            yyn = yyExca[xi + 1];
            if (yyn < 0)
            {
                return 0;
            }
        }
        if (yyn == 0)
        {
            // error ... attempt to resume parsing
            if (errflag == 0)
            {
                yylex.Error();
            }
            if (errflag <= 2)
            {
                errflag = 3;
                // find a state where "error" is a legal shift action
                while (yyp >= 0)
                {
                    yyn = yyPact[yyS[yyp].Yys] + yyErrCode;
                    if (yyn >= 0 && yyn < yyLast)
                    {
                        yystate = yyAct[yyn]; // simulate a shift of "error"
                        if (yyChk[yystate] == yyErrCode)
                        {
                            goto yystack;
                        }
                    }
                    yyp--;
                }
                return 1;
            }
            // no shift yet; clobber input char
            if (yytoken == yyEofCode)
            {
                return 1;
            }
            yychar = -1;
            yytoken = -1;
            goto yynewstate;
        }

        // reduction by production yyn
        int yynt = yyn;
        int yypt = yyp;
        yyp -= yyR2[yyn];
        if (yyp + 1 >= yyS.Length)
        {
            Array.Resize(ref yyS, yyS.Length * 2);
        }
        yyVAL = yyS[yyp + 1];

        // consult goto table to find next state
        yyn = yyR1[yyn];
        int yyg = yyPgo[yyn];
        int yyj = yyg + yyS[yyp].Yys + 1;
        if (yyj >= yyLast)
        {
            yystate = yyAct[yyg];
        }
        else
        {
            yystate = yyAct[yyj];
            if (yyChk[yystate] != -yyn)
            {
                yystate = yyAct[yyg];
            }
        }
        Reduce(yynt, yyS, yypt, ref yyVAL, yylex);
        goto yystack;
    }

    // s[top] is the last symbol of the rule, so $k of an n-symbol rule is s[top - n + k].
    private static void Reduce(int rule, SymType[] s, int top, ref SymType yyVAL, Lexer yylex)
    {
        switch (rule)
        {
            case 1:
            {
                JqQuery query = Q(s[top].Value);
                query.Meta = (ConstObject?)s[top - 2].Value;
                query.Imports = (List<Import>?)s[top - 1].Value;
                yylex.Result = query;
                break;
            }
            case 2:
            case 4:
            case 8:
            case 12:
            case 100:
            case 102:
            case 104:
                yyVAL.Value = null;
                break;
            case 3:
                yyVAL.Value = s[top - 1].Value;
                break;
            case 5:
                yyVAL.Value = Append((List<Import>?)s[top - 1].Value, (Import)s[top].Value!);
                break;
            case 6:
                yyVAL.Value = new Import { ImportPath = s[top - 4].Token, ImportAlias = s[top - 2].Token, Meta = (ConstObject?)s[top - 1].Value };
                break;
            case 7:
                yyVAL.Value = new Import { IncludePath = s[top - 2].Token, Meta = (ConstObject?)s[top - 1].Value };
                break;
            case 10:
            {
                List<FuncDef>? funcdefs = (List<FuncDef>?)s[top].Value;
                funcdefs?.Reverse();
                yyVAL.Value = new JqQuery { FuncDefs = funcdefs };
                break;
            }
            case 13:
                yyVAL.Value = Append((List<FuncDef>?)s[top].Value, (FuncDef)s[top - 1].Value!);
                break;
            case 14:
                yyVAL.Value = new FuncDef { Name = s[top - 3].Token, Body = Q(s[top - 1].Value) };
                break;
            case 15:
                yyVAL.Value = new FuncDef { Name = s[top - 6].Token, Args = (List<string>)s[top - 4].Value!, Body = Q(s[top - 1].Value) };
                break;
            case 16:
                yyVAL.Value = new List<string> { s[top].Token };
                break;
            case 17:
                yyVAL.Value = Append((List<string>)s[top - 2].Value!, s[top].Token);
                break;
            case 20:
            {
                JqQuery query = Q(s[top].Value);
                query.FuncDefs ??= new List<FuncDef>();
                query.FuncDefs.Insert(0, (FuncDef)s[top - 1].Value!);
                yyVAL.Value = query;
                break;
            }
            case 21:
            case 116:
                yyVAL.Value = Bin(s[top - 2].Value, Operator.Pipe, s[top].Value);
                break;
            case 22:
                yyVAL.Value = new JqQuery { Left = Q(s[top - 4].Value), Op = Operator.Pipe, Right = Q(s[top].Value), Patterns = (List<Pattern>)s[top - 2].Value! };
                break;
            case 23:
                yyVAL.Value = new JqQuery { Term = new Term { Type = TermType.Label, Label = new Label { Ident = s[top - 2].Token, Body = Q(s[top].Value) } } };
                break;
            case 24:
                yyVAL.Value = Bin(s[top - 2].Value, Operator.Comma, s[top].Value);
                break;
            case 26:
            case 27:
            case 30:
                yyVAL.Value = Bin(s[top - 2].Value, s[top - 1].Operator, s[top].Value);
                break;
            case 28:
                yyVAL.Value = Bin(s[top - 2].Value, Operator.Or, s[top].Value);
                break;
            case 29:
                yyVAL.Value = Bin(s[top - 2].Value, Operator.And, s[top].Value);
                break;
            case 31:
                yyVAL.Value = Bin(s[top - 2].Value, Operator.Add, s[top].Value);
                break;
            case 32:
                yyVAL.Value = Bin(s[top - 2].Value, Operator.Sub, s[top].Value);
                break;
            case 33:
                yyVAL.Value = Bin(s[top - 2].Value, Operator.Mul, s[top].Value);
                break;
            case 34:
                yyVAL.Value = Bin(s[top - 2].Value, Operator.Div, s[top].Value);
                break;
            case 35:
                yyVAL.Value = Bin(s[top - 2].Value, Operator.Mod, s[top].Value);
                break;
            case 36:
                yyVAL.Value = new JqQuery { Term = (Term)s[top].Value! };
                break;
            case 37:
            case 42:
                yyVAL.Value = new List<Pattern> { (Pattern)s[top].Value! };
                break;
            case 38:
                yyVAL.Value = Append((List<Pattern>)s[top - 3].Value!, (Pattern)s[top].Value!);
                break;
            case 39:
                yyVAL.Value = new Pattern { Name = s[top].Token };
                break;
            case 40:
                yyVAL.Value = new Pattern { Array = (List<Pattern>)s[top - 1].Value! };
                break;
            case 41:
                yyVAL.Value = new Pattern { Object = (List<PatternObject>)s[top - 1].Value! };
                break;
            case 43:
                yyVAL.Value = Append((List<Pattern>)s[top - 2].Value!, (Pattern)s[top].Value!);
                break;
            case 44:
                yyVAL.Value = new List<PatternObject> { (PatternObject)s[top].Value! };
                break;
            case 45:
                yyVAL.Value = Append((List<PatternObject>)s[top - 2].Value!, (PatternObject)s[top].Value!);
                break;
            case 46:
                yyVAL.Value = new PatternObject { Key = s[top - 2].Token, Val = (Pattern)s[top].Value! };
                break;
            case 47:
                yyVAL.Value = new PatternObject { KeyString = (StringTerm)s[top - 2].Value!, Val = (Pattern)s[top].Value! };
                break;
            case 48:
                yyVAL.Value = new PatternObject { KeyQuery = Q(s[top - 3].Value), Val = (Pattern)s[top].Value! };
                break;
            case 49:
                yyVAL.Value = new PatternObject { Key = s[top].Token };
                break;
            case 50:
                yyVAL.Value = new Term { Type = TermType.Identity };
                break;
            case 51:
                yyVAL.Value = new Term { Type = TermType.Recurse };
                break;
            case 52:
                yyVAL.Value = new Term { Type = TermType.Index, Index = new IndexTerm { Name = s[top].Token } };
                break;
            case 53:
            {
                Suffix suffix = (Suffix)s[top].Value!;
                yyVAL.Value = suffix.Iter
                    ? new Term { Type = TermType.Identity, SuffixList = new List<Suffix> { suffix } }
                    : new Term { Type = TermType.Index, Index = suffix.Index };
                break;
            }
            case 54:
                yyVAL.Value = new Term { Type = TermType.Index, Index = new IndexTerm { Str = (StringTerm)s[top].Value! } };
                break;
            case 55:
                yyVAL.Value = new Term { Type = TermType.Null };
                break;
            case 56:
                yyVAL.Value = new Term { Type = TermType.True };
                break;
            case 57:
                yyVAL.Value = new Term { Type = TermType.False };
                break;
            case 58:
            case 60:
                yyVAL.Value = new Term { Type = TermType.Func, Func = new FuncCall { Name = s[top].Token } };
                break;
            case 59:
                yyVAL.Value = new Term { Type = TermType.Func, Func = new FuncCall { Name = s[top - 3].Token, Args = (List<JqQuery>)s[top - 1].Value! } };
                break;
            case 61:
                yyVAL.Value = new Term { Type = TermType.Object, Object = new ObjectTerm() };
                break;
            case 62:
                yyVAL.Value = new Term { Type = TermType.Object, Object = new ObjectTerm { KeyVals = (List<ObjectKeyVal>)s[top - 1].Value! } };
                break;
            case 63:
                yyVAL.Value = new Term { Type = TermType.Object, Object = new ObjectTerm { KeyVals = (List<ObjectKeyVal>)s[top - 2].Value! } };
                break;
            case 64:
                yyVAL.Value = new Term { Type = TermType.Array, Array = new ArrayTerm() };
                break;
            case 65:
                yyVAL.Value = new Term { Type = TermType.Array, Array = new ArrayTerm { Query = Q(s[top - 1].Value) } };
                break;
            case 66:
                yyVAL.Value = new Term { Type = TermType.Number, Number = s[top].Token };
                break;
            case 67:
                yyVAL.Value = new Term { Type = TermType.Unary, Unary = new Unary { Op = Operator.Add, Term = (Term)s[top].Value! } };
                break;
            case 68:
                yyVAL.Value = new Term { Type = TermType.Unary, Unary = new Unary { Op = Operator.Sub, Term = (Term)s[top].Value! } };
                break;
            case 69:
                yyVAL.Value = new Term { Type = TermType.Format, Format = s[top].Token };
                break;
            case 70:
                yyVAL.Value = new Term { Type = TermType.Format, Format = s[top - 1].Token, Str = (StringTerm)s[top].Value! };
                break;
            case 71:
                yyVAL.Value = new Term { Type = TermType.String, Str = (StringTerm)s[top].Value! };
                break;
            case 72:
                yyVAL.Value = new Term
                {
                    Type = TermType.If,
                    If = new IfTerm { Cond = Q(s[top - 5].Value), Then = Q(s[top - 3].Value), Elif = (List<IfElif>?)s[top - 2].Value, Else = (JqQuery?)s[top - 1].Value },
                };
                break;
            case 73:
                yyVAL.Value = new Term { Type = TermType.Try, Try = new TryTerm { Body = Q(s[top - 1].Value), Catch = (JqQuery?)s[top].Value } };
                break;
            case 74:
                yyVAL.Value = new Term
                {
                    Type = TermType.Reduce,
                    Reduce = new Reduce { Query = Q(s[top - 7].Value), Patterns = (List<Pattern>)s[top - 5].Value!, Start = Q(s[top - 3].Value), Update = Q(s[top - 1].Value) },
                };
                break;
            case 75:
                yyVAL.Value = new Term
                {
                    Type = TermType.Foreach,
                    Foreach = new Foreach { Query = Q(s[top - 7].Value), Patterns = (List<Pattern>)s[top - 5].Value!, Start = Q(s[top - 3].Value), Update = Q(s[top - 1].Value) },
                };
                break;
            case 76:
                yyVAL.Value = new Term
                {
                    Type = TermType.Foreach,
                    Foreach = new Foreach { Query = Q(s[top - 9].Value), Patterns = (List<Pattern>)s[top - 7].Value!, Start = Q(s[top - 5].Value), Update = Q(s[top - 3].Value), Extract = Q(s[top - 1].Value) },
                };
                break;
            case 77:
                yyVAL.Value = new Term { Type = TermType.Break, Break = s[top].Token };
                break;
            case 78:
                yyVAL.Value = new Term { Type = TermType.Query, Query = Q(s[top - 1].Value) };
                break;
            case 79:
                AddSuffix(s[top - 1].Value, new Suffix { Index = new IndexTerm { Name = s[top].Token } });
                break;
            case 80:
                AddSuffix(s[top - 1].Value, (Suffix)s[top].Value!);
                break;
            case 81:
                AddSuffix(s[top - 1].Value, new Suffix { Optional = true });
                break;
            case 82:
                AddSuffix(s[top - 2].Value, (Suffix)s[top].Value!);
                break;
            case 83:
                AddSuffix(s[top - 2].Value, new Suffix { Index = new IndexTerm { Str = (StringTerm)s[top].Value! } });
                break;
            case 84:
                yyVAL.Value = new StringTerm { Str = s[top].Token };
                break;
            case 85:
                yyVAL.Value = new StringTerm { Queries = (List<JqQuery>)s[top - 1].Value! };
                break;
            case 86:
                yyVAL.Value = new List<JqQuery>();
                break;
            case 87:
                yyVAL.Value = Append((List<JqQuery>)s[top - 1].Value!, new JqQuery { Term = new Term { Type = TermType.String, Str = new StringTerm { Str = s[top].Token } } });
                break;
            case 88:
                yylex.InString = true;
                yyVAL.Value = Append((List<JqQuery>)s[top - 3].Value!, new JqQuery { Term = new Term { Type = TermType.Query, Query = Q(s[top - 1].Value) } });
                break;
            case 93:
                yyVAL.Value = new Suffix { Iter = true };
                break;
            case 94:
                yyVAL.Value = new Suffix { Index = new IndexTerm { Start = Q(s[top - 1].Value) } };
                break;
            case 95:
                yyVAL.Value = new Suffix { Index = new IndexTerm { Start = Q(s[top - 2].Value), IsSlice = true } };
                break;
            case 96:
                yyVAL.Value = new Suffix { Index = new IndexTerm { End = Q(s[top - 1].Value), IsSlice = true } };
                break;
            case 97:
                yyVAL.Value = new Suffix { Index = new IndexTerm { Start = Q(s[top - 3].Value), End = Q(s[top - 1].Value), IsSlice = true } };
                break;
            case 98:
                yyVAL.Value = new List<JqQuery> { Q(s[top].Value) };
                break;
            case 99:
                yyVAL.Value = Append((List<JqQuery>)s[top - 2].Value!, Q(s[top].Value));
                break;
            case 101:
                yyVAL.Value = Append((List<IfElif>?)s[top - 4].Value, new IfElif { Cond = Q(s[top - 2].Value), Then = Q(s[top].Value) });
                break;
            case 103:
            case 105:
                yyVAL.Value = s[top].Value;
                break;
            case 106:
                yyVAL.Value = new List<ObjectKeyVal> { (ObjectKeyVal)s[top].Value! };
                break;
            case 107:
                yyVAL.Value = Append((List<ObjectKeyVal>)s[top - 2].Value!, (ObjectKeyVal)s[top].Value!);
                break;
            case 108:
                yyVAL.Value = new ObjectKeyVal { Key = s[top - 2].Token, Val = Q(s[top].Value) };
                break;
            case 109:
                yyVAL.Value = new ObjectKeyVal { KeyString = (StringTerm)s[top - 2].Value!, Val = Q(s[top].Value) };
                break;
            case 110:
                yyVAL.Value = new ObjectKeyVal { KeyQuery = Q(s[top - 3].Value), Val = Q(s[top].Value) };
                break;
            case 111:
                yyVAL.Value = new ObjectKeyVal { Key = s[top].Token };
                break;
            case 112:
                yyVAL.Value = new ObjectKeyVal { KeyString = (StringTerm)s[top].Value! };
                break;
            case 118:
                yyVAL.Value = new ConstTerm { Object = (ConstObject)s[top].Value! };
                break;
            case 119:
                yyVAL.Value = new ConstTerm { Array = (ConstArray)s[top].Value! };
                break;
            case 120:
                yyVAL.Value = new ConstTerm { Number = s[top].Token };
                break;
            case 121:
                yyVAL.Value = new ConstTerm { Str = s[top].Token };
                break;
            case 122:
                yyVAL.Value = new ConstTerm { Null = true };
                break;
            case 123:
                yyVAL.Value = new ConstTerm { True = true };
                break;
            case 124:
                yyVAL.Value = new ConstTerm { False = true };
                break;
            case 125:
                yyVAL.Value = new ConstObject();
                break;
            case 126:
                yyVAL.Value = new ConstObject { KeyVals = (List<ConstObjectKeyVal>)s[top - 1].Value! };
                break;
            case 127:
                yyVAL.Value = new ConstObject { KeyVals = (List<ConstObjectKeyVal>)s[top - 2].Value! };
                break;
            case 128:
                yyVAL.Value = new List<ConstObjectKeyVal> { (ConstObjectKeyVal)s[top].Value! };
                break;
            case 129:
                yyVAL.Value = Append((List<ConstObjectKeyVal>)s[top - 2].Value!, (ConstObjectKeyVal)s[top].Value!);
                break;
            case 130:
            case 131:
                yyVAL.Value = new ConstObjectKeyVal { Key = s[top - 2].Token, Val = (ConstTerm)s[top].Value! };
                break;
            case 132:
                yyVAL.Value = new ConstObjectKeyVal { KeyString = s[top - 2].Token, Val = (ConstTerm)s[top].Value! };
                break;
            case 133:
                yyVAL.Value = new ConstArray();
                break;
            case 134:
                yyVAL.Value = new ConstArray { Elems = (List<ConstTerm>)s[top - 1].Value! };
                break;
            case 135:
                yyVAL.Value = new List<ConstTerm> { (ConstTerm)s[top].Value! };
                break;
            case 136:
                yyVAL.Value = Append((List<ConstTerm>)s[top - 2].Value!, (ConstTerm)s[top].Value!);
                break;
        }
    }

    private static JqQuery Q(object? v) => (JqQuery)v!;

    private static JqQuery Bin(object? l, Operator op, object? r) => new() { Left = Q(l), Op = op, Right = Q(r) };

    private static void AddSuffix(object? term, Suffix suffix)
    {
        Term t = (Term)term!;
        (t.SuffixList ??= new List<Suffix>()).Add(suffix);
    }

    private static List<T> Append<T>(List<T>? list, T item)
    {
        list ??= new List<T>();
        list.Add(item);
        return list;
    }
}
