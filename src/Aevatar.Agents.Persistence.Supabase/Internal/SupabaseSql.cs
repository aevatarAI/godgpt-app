using System.Text.RegularExpressions;

namespace Aevatar.Agents.Persistence.Supabase.Internal;

/// <summary>
/// Supabase(Postgres) SQL 相关的小工具：
/// - 标识符校验（防 SQL 注入）
/// - schema.table 拼接
/// - FTS regconfig literal 生成
/// </summary>
internal static class SupabaseSql
{
    // 只允许：全小写 + 下划线 + 数字，且以字母开头
    // 这样我们可以不使用引号，避免大小写/转义坑，同时彻底杜绝注入。
    private static readonly Regex IdentifierRegex = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

    internal static string Ident(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Identifier cannot be null/empty.", paramName);
        }

        value = value.Trim();
        if (!IdentifierRegex.IsMatch(value))
        {
            throw new ArgumentException(
                $"Invalid identifier '{value}'. Only [a-z][a-z0-9_]* is allowed.",
                paramName);
        }

        return value;
    }

    internal static string Table(string schema, string table, string paramNameForTable)
    {
        var s = Ident(schema, nameof(schema));
        var t = Ident(table, paramNameForTable);
        return $"{s}.{t}";
    }

    /// <summary>
    /// 生成一个安全的 regconfig SQL 字符串字面量，例如：'simple'
    /// </summary>
    internal static string RegConfigLiteral(string regConfig)
    {
        var cfg = Ident(regConfig, nameof(regConfig));
        return $"'{cfg}'";
    }
}


