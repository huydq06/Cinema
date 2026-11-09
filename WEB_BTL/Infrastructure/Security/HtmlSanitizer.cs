using System.Text.RegularExpressions;
using System.Net;

namespace WEB_BTL.Infrastructure.Security;

/// <summary>
/// Sanitize HTML bằng allowlist: chỉ giữ các thẻ định dạng văn bản và thuộc tính an toàn.
/// Không phải trình sanitize đầy đủ như HtmlSanitizer của NuGet, nhưng chặn script,
/// sự kiện inline, javascript:/data: URL và thẻ nhúng ngoài — đủ cho mô tả phim do Admin nhập.
/// </summary>
public static partial class HtmlSanitizer
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "strong", "b", "em", "i", "u", "s", "ul", "ol", "li",
        "h2", "h3", "h4", "blockquote", "a", "span", "div"
    };

    private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "title", "target", "rel"
    };

    private static readonly HashSet<string> SafeSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "mailto"
    };

    public static string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        // Bỏ hẳn nội dung nguy hiểm kèm thẻ đóng/mở.
        var cleaned = DangerousBlockRegex().Replace(html, string.Empty);

        // Xử lý từng thẻ: giữ thẻ cho phép với thuộc tính cho phép, bỏ phần còn lại.
        cleaned = TagRegex().Replace(cleaned, match =>
        {
            var isClosing = match.Groups["close"].Success;
            var name = match.Groups["name"].Value;

            if (!AllowedTags.Contains(name))
            {
                return string.Empty;
            }

            if (isClosing)
            {
                return $"</{name.ToLowerInvariant()}>";
            }

            var attributes = AttributeRegex().Matches(match.Groups["attrs"].Value)
                .Select(attr =>
                {
                    var attrName = attr.Groups["an"].Value;
                    var attrValue = WebUtility.HtmlDecode(attr.Groups["av"].Value);

                    if (!AllowedAttributes.Contains(attrName))
                    {
                        return null;
                    }

                    if (attrName.Equals("href", StringComparison.OrdinalIgnoreCase)
                        && !IsSafeUrl(attrValue))
                    {
                        return null;
                    }

                    return $" {attrName.ToLowerInvariant()}=\"{WebUtility.HtmlEncode(attrValue)}\"";
                })
                .Where(x => x is not null)
                .ToArray();

            return $"<{name.ToLowerInvariant()}{string.Concat(attributes)}>";
        });

        return cleaned.Trim();
    }

    private static bool IsSafeUrl(string value)
    {
        var trimmed = value.Trim();
        var colon = trimmed.IndexOf(':');

        // Không có scheme (đường dẫn tương đối) hoặc scheme an toàn.
        if (colon < 0)
        {
            return !trimmed.StartsWith("//", StringComparison.Ordinal);
        }

        var scheme = trimmed[..colon];
        return SafeSchemes.Contains(scheme)
            && !trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"<\s*(script|style|iframe|object|embed|form)\b[^>]*>.*?<\s*/\s*\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DangerousBlockRegex();

    [GeneratedRegex(@"<\s*(?<close>/\s*)?(?<name>[a-zA-Z][a-zA-Z0-9]*)(?<attrs>(?:\s+[^<>]*?)?)\s*/?\s*>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"(?<an>[a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*=\s*(?<av>""[^""]*""|'[^']*'|[^\s"">]+)")]
    private static partial Regex AttributeRegex();
}
