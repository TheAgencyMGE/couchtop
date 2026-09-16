using System.IO.Compression;
using Couchtop.Core.Files;
using Xunit;

namespace Couchtop.Tests;

public sealed class FileFormatTests
{
    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1, "1 byte")]
    [InlineData(999, "999 bytes")]
    [InlineData(1024, "1.00 KB")]
    [InlineData(1536, "1.50 KB")]
    [InlineData(10 * 1024, "10.0 KB")]
    [InlineData(150 * 1024, "150 KB")]
    [InlineData(3_500_000, "3.34 MB")]
    [InlineData(5_000_000_000, "4.66 GB")]
    public void Sizes_read_like_a_file_manager(long bytes, string expected) => Assert.Equal(expected, FileFormat.Size(bytes));

    [Fact]
    public void Times_shorten_for_today_and_this_year()
    {
        Assert.DoesNotContain(DateTime.Today.Year.ToString(), FileFormat.When(DateTime.Now));
        Assert.Contains("2019", FileFormat.When(new DateTime(2019, 5, 4, 10, 0, 0)));
    }
}

public sealed class FileOperationTests
{
    [Fact]
    public void Copies_get_a_number_rather_than_overwriting()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "report.txt", "report (2).txt" };
        Assert.Equal("report (3).txt", FileOperations.UniqueName("report.txt", taken.Contains));
        Assert.Equal("fresh.txt", FileOperations.UniqueName("fresh.txt", taken.Contains));
    }

    [Fact]
    public void Folders_without_an_extension_are_numbered_too()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Photos" };
        Assert.Equal("Photos (2)", FileOperations.UniqueName("Photos", taken.Contains));
    }

    [Theory]
    [InlineData("notes.txt", true)]
    [InlineData("a name with spaces.pdf", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("bad/name.txt", false)]
    [InlineData("bad:name.txt", false)]
    [InlineData("trailing.", false)]
    [InlineData(" leading.txt", false)]
    [InlineData("CON", false)]
    [InlineData("nul.txt", false)]
    public void Names_are_checked_before_renaming(string name, bool valid) => Assert.Equal(valid, FileOperations.IsValidName(name));

    [Theory]
    [InlineData(@"C:\work", @"C:\work", true)]
    [InlineData(@"C:\work", @"C:\work\inner", true)]
    [InlineData(@"C:\work\", @"C:\work\inner\deep", true)]
    [InlineData(@"C:\work", @"C:\workshop", false)]
    [InlineData(@"C:\work", @"C:\other", false)]
    public void A_folder_cannot_be_copied_into_itself(string source, string destination, bool blocked) =>
        Assert.Equal(blocked, FileOperations.IsInsideItself(source, destination));
}

public sealed class ArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "couchtop-files-" + Guid.NewGuid().ToString("N"));

    public ArchiveTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void A_selection_is_zipped_and_can_be_extracted_again()
    {
        var folder = Path.Combine(_root, "notes");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "one.txt"), "first");
        File.WriteAllText(Path.Combine(_root, "loose.txt"), "second");

        var archive = Archives.Compress(new[] { folder, Path.Combine(_root, "loose.txt") }, _root, File.Exists);
        Assert.True(File.Exists(archive));
        Assert.True(Archives.IsArchive(archive));

        using (var zip = ZipFile.OpenRead(archive))
        {
            Assert.Contains(zip.Entries, e => e.FullName == "notes/one.txt");
            Assert.Contains(zip.Entries, e => e.FullName == "loose.txt");
        }

        var extracted = Archives.ExtractHere(archive, p => File.Exists(p) || Directory.Exists(p));
        Assert.True(Directory.Exists(extracted));
        Assert.Equal("first", File.ReadAllText(Path.Combine(extracted, "notes", "one.txt")));
    }

    [Fact]
    public void Extracting_twice_does_not_overwrite_the_first_folder()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "x");
        var archive = Archives.Compress(new[] { Path.Combine(_root, "a.txt") }, _root, File.Exists);

        bool Exists(string p) => File.Exists(p) || Directory.Exists(p);
        var first = Archives.ExtractHere(archive, Exists);
        var second = Archives.ExtractHere(archive, Exists);
        Assert.NotEqual(first, second);
        Assert.True(Directory.Exists(first) && Directory.Exists(second));
    }

    [Fact]
    public void Copying_a_tree_keeps_its_shape()
    {
        var source = Path.Combine(_root, "src");
        Directory.CreateDirectory(Path.Combine(source, "inner"));
        File.WriteAllText(Path.Combine(source, "top.txt"), "1");
        File.WriteAllText(Path.Combine(source, "inner", "deep.txt"), "2");

        var destination = Path.Combine(_root, "copy");
        FileOperations.CopyDirectory(source, destination, CancellationToken.None);

        Assert.Equal("1", File.ReadAllText(Path.Combine(destination, "top.txt")));
        Assert.Equal("2", File.ReadAllText(Path.Combine(destination, "inner", "deep.txt")));
    }

    [Fact]
    public void Details_are_read_for_a_folder_and_a_file()
    {
        var folder = Path.Combine(_root, "measure");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "a.bin"), new string('x', 2048));

        var folderDetails = ItemInspector.Read(folder);
        Assert.True(folderDetails.IsFolder);
        Assert.Equal(1, folderDetails.Items);
        Assert.Equal(2048, folderDetails.Size);

        var fileDetails = ItemInspector.Read(Path.Combine(folder, "a.bin"));
        Assert.False(fileDetails.IsFolder);
        Assert.Equal(2048, fileDetails.Size);
        Assert.Equal("a.bin", fileDetails.Name);
    }
}

public sealed class PlacesTests
{
    [Fact]
    public void Drives_are_listed_with_readable_names()
    {
        var drives = Places.Drives();
        Assert.NotEmpty(drives);
        Assert.All(drives, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Name));
            Assert.False(string.IsNullOrWhiteSpace(d.Path));
        });
        Assert.Contains(drives, d => d.Kind == PlaceKind.FixedDrive);
    }

    [Fact]
    public void User_folders_all_exist()
    {
        foreach (var place in Places.UserFolders()) Assert.True(Directory.Exists(place.Path));
    }
}
