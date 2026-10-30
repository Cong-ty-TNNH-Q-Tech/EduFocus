using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FlowList = System.Windows.Documents.List;

namespace EduFocus.WPF.Services;

/// <summary>
/// Renders the embedded privacy notice (docs/privacy-notice.md) into a
/// FlowDocument. Only the small Markdown subset used by that file is
/// supported: headings, paragraphs, bullets, bold, inline code, links and
/// horizontal rules.
/// </summary>
internal static class PrivacyNoticeRenderer
{
    private const string ResourceName = "EduFocus.Resources.privacy-notice.md";

    private static readonly Regex InlinePattern = new(
        @"\*\*(?<bold>.+?)\*\*|`(?<code>[^`]+)`|\[(?<text>[^\]]+)\]\((?<url>[^)\s]+)\)|<(?<auto>https?://[^>\s]+)>",
        RegexOptions.Compiled);

    private static readonly Brush HeadingBrush = Freeze(Color.FromRgb(0x1F, 0x24, 0x30));
    private static readonly Brush BodyBrush = Freeze(Color.FromRgb(0x3A, 0x40, 0x4D));
    private static readonly Brush MutedBrush = Freeze(Color.FromRgb(0x5B, 0x62, 0x70));
    private static readonly Brush LinkBrush = Freeze(Color.FromRgb(0x2F, 0x6F, 0xED));
    private static readonly Brush CodeBrush = Freeze(Color.FromRgb(0xEE, 0xF1, 0xF7));
    private static readonly Brush SeparatorBrush = Freeze(Color.FromRgb(0xE1, 0xE4, 0xEC));

    public static FlowDocument CreateDocument() => BuildDocument(ReadEmbeddedNotice());

    public static FlowDocument CreateErrorDocument(Exception exception)
    {
        var document = CreateBaseDocument();
        document.Blocks.Add(new Paragraph(new Bold(new Run("Không thể hiển thị thông báo quyền riêng tư.")))
        {
            FontSize = 18,
            Foreground = HeadingBrush,
            Margin = new Thickness(0, 0, 0, 8)
        });
        document.Blocks.Add(new Paragraph(new Run(
            "Tệp thông báo nhúng bị thiếu hoặc hỏng. Bản đầy đủ được cài tại " +
            "docs\\privacy-notice.md trong thư mục cài đặt EduFocus. Chi tiết lỗi: " +
            exception.Message))
        {
            Margin = new Thickness(0, 0, 0, 10)
        });
        return document;
    }

    internal static string ReadEmbeddedNotice()
    {
        using var stream = typeof(PrivacyNoticeRenderer).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
            throw new InvalidOperationException($"Embedded resource '{ResourceName}' was not found.");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    internal static FlowDocument BuildDocument(string markdown)
    {
        var document = CreateBaseDocument();
        var paragraphBuilder = new StringBuilder();
        FlowList? bulletList = null;
        Paragraph? currentBullet = null;

        void FlushParagraph()
        {
            if (paragraphBuilder.Length == 0)
                return;

            var text = paragraphBuilder.ToString().Trim();
            paragraphBuilder.Clear();
            if (text.Length == 0)
                return;

            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 10) };
            AppendInlines(paragraph.Inlines, text);
            document.Blocks.Add(paragraph);
        }

        void AddBullet(string text)
        {
            if (bulletList is null)
            {
                bulletList = new FlowList
                {
                    MarkerStyle = TextMarkerStyle.Disc,
                    Margin = new Thickness(20, 0, 0, 10),
                    Padding = new Thickness(0)
                };
                document.Blocks.Add(bulletList);
            }

            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
            AppendInlines(paragraph.Inlines, text);
            bulletList.ListItems.Add(new ListItem(paragraph));
            currentBullet = paragraph;
        }

        foreach (var rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.TrimStart();

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                currentBullet = null;
                continue;
            }

            if (trimmed == "---")
            {
                FlushParagraph();
                currentBullet = null;
                bulletList = null;
                document.Blocks.Add(new BlockUIContainer(new Border
                {
                    Height = 1,
                    Background = SeparatorBrush,
                    Margin = new Thickness(0, 10, 0, 14)
                }));
                continue;
            }

            if (trimmed[0] == '#')
            {
                FlushParagraph();
                currentBullet = null;
                bulletList = null;

                var level = trimmed.TakeWhile(character => character == '#').Count();
                var text = trimmed[level..].Trim();
                var heading = new Paragraph
                {
                    FontWeight = FontWeights.SemiBold,
                    Foreground = HeadingBrush,
                    Margin = new Thickness(0, level == 1 ? 0 : 18, 0, level == 1 ? 8 : 6)
                };

                switch (level)
                {
                    case 1:
                        heading.FontSize = 24;
                        break;
                    case 2:
                        heading.FontSize = 18;
                        heading.BorderBrush = SeparatorBrush;
                        heading.BorderThickness = new Thickness(0, 0, 0, 1);
                        heading.Padding = new Thickness(0, 0, 0, 5);
                        break;
                    default:
                        heading.FontSize = 15;
                        break;
                }

                AppendInlines(heading.Inlines, text);
                document.Blocks.Add(heading);
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                FlushParagraph();
                AddBullet(trimmed[2..].Trim());
                continue;
            }

            var isIndented = line.Length > trimmed.Length;
            if (isIndented && currentBullet is not null)
            {
                AppendInlines(currentBullet.Inlines, " " + trimmed);
                continue;
            }

            if (isIndented && paragraphBuilder.Length > 0)
            {
                paragraphBuilder.Append(' ').Append(trimmed);
                continue;
            }

            currentBullet = null;
            bulletList = null;

            if (paragraphBuilder.Length > 0)
                paragraphBuilder.Append(' ');
            paragraphBuilder.Append(trimmed);
        }

        FlushParagraph();
        return document;
    }

    private static FlowDocument CreateBaseDocument()
    {
        return new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            Foreground = BodyBrush,
            PagePadding = new Thickness(4),
            LineHeight = 21,
            ColumnWidth = double.PositiveInfinity
        };
    }

    private static void AppendInlines(InlineCollection target, string text)
    {
        var index = 0;
        foreach (Match match in InlinePattern.Matches(text))
        {
            if (match.Index > index)
                target.Add(new Run(text[index..match.Index]));

            if (match.Groups["bold"].Success)
            {
                target.Add(new Bold(new Run(match.Groups["bold"].Value)));
            }
            else if (match.Groups["code"].Success)
            {
                target.Add(new Run(match.Groups["code"].Value)
                {
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 13,
                    Background = CodeBrush
                });
            }
            else if (match.Groups["text"].Success)
            {
                target.Add(CreateHyperlink(match.Groups["text"].Value, match.Groups["url"].Value));
            }
            else if (match.Groups["auto"].Success)
            {
                var url = match.Groups["auto"].Value;
                target.Add(CreateHyperlink(url, url));
            }

            index = match.Index + match.Length;
        }

        if (index < text.Length)
            target.Add(new Run(text[index..]));
    }

    private static Hyperlink CreateHyperlink(string text, string url)
    {
        var hyperlink = new Hyperlink(new Run(text))
        {
            Foreground = LinkBrush,
            TextDecorations = TextDecorations.Underline,
            ToolTip = url
        };

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            hyperlink.NavigateUri = uri;
            hyperlink.RequestNavigate += (_, args) => OpenExternal(args.Uri);
        }
        else
        {
            hyperlink.Foreground = MutedBrush;
        }

        return hyperlink;
    }

    private static void OpenExternal(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https"))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Opening the browser is best-effort; never crash the notice viewer.
        }
    }

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
