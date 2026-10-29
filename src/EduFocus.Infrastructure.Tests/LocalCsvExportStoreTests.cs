using System.Text;
using EduFocus.Infrastructure.Export;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests;

public sealed class LocalCsvExportStoreTests : IDisposable
{
    private readonly string _root;

    public LocalCsvExportStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "edufocus-export-tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task Save_writes_utf8_bom_so_excel_reads_vietnamese()
    {
        const string content = "Loại bản ghi,Ghi chú\r\nTổng hợp,Xuất lúc 10:05:06\r\n";
        var store = CreateStore();

        var path = await store.SaveAsync("bao-cao.csv", content);

        var bytes = await File.ReadAllBytesAsync(path);
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Should().Be(content);
        Path.GetFullPath(path).Should().Be(Path.Combine(Path.GetFullPath(_root), "bao-cao.csv"));
    }

    [Fact]
    public async Task Save_creates_the_export_directory()
    {
        var store = CreateStore();

        var path = await store.SaveAsync("bao-cao.csv", "data");

        Directory.Exists(_root).Should().BeTrue();
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task Save_never_overwrites_an_existing_file()
    {
        var store = CreateStore();
        var first = await store.SaveAsync("bao-cao.csv", "first");
        var second = await store.SaveAsync("bao-cao.csv", "second");
        var third = await store.SaveAsync("bao-cao.csv", "third");

        first.Should().NotBe(second);
        second.Should().NotBe(third);
        Path.GetFileName(second).Should().Be("bao-cao-2.csv");
        Path.GetFileName(third).Should().Be("bao-cao-3.csv");
        (await File.ReadAllTextAsync(first)).Should().Be("first");
        (await File.ReadAllTextAsync(second)).Should().Be("second");
        (await File.ReadAllTextAsync(third)).Should().Be("third");
    }

    [Theory]
    [InlineData("../secret.csv")]
    [InlineData("..\\secret.csv")]
    [InlineData("folder/name.csv")]
    [InlineData("folder\\name.csv")]
    public async Task Save_rejects_names_that_escape_the_export_directory(string fileName)
    {
        var store = CreateStore();

        var act = () => store.SaveAsync(fileName, "data");

        await act.Should().ThrowAsync<ArgumentException>();
        Directory.Exists(Path.Combine(_root, "folder")).Should().BeFalse();
        File.Exists(Path.Combine(Path.GetTempPath(), "secret.csv")).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Save_rejects_blank_file_names(string fileName)
    {
        var store = CreateStore();

        var act = () => store.SaveAsync(fileName, "data");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private LocalCsvExportStore CreateStore() =>
        new(new CsvExportOptions { DirectoryPath = _root });
}
