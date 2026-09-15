using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Commands;
using Wms.Application.LicensePlateNumbers;
using Wms.Common;
using Wms.Data;
using Wms.WebApp.Printing;
using ZXing;

// Only this uniquely named disposable LocalDB database is touched.
var database = "WmsLpn_" + Guid.NewGuid().ToString("N");
var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true;TrustServerCertificate=true";
using var provider = new ServiceCollection().Configure<IdentityOptions>(o =>
    o.Stores.SchemaVersion = IdentitySchemaVersions.Version3).BuildServiceProvider();
var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseApplicationServiceProvider(provider)
    .UseSqlServer(connection).Options;
var factory = new ContextFactory(options);
LicensePlateNumberService Service(IDbContextFactory<ApplicationDbContext> f) => new(new CommandExecutor(f), f);
LicensePlateNumberService Racing() => Service(new ContextFactory(
    new DbContextOptionsBuilder<ApplicationDbContext>(options).AddInterceptors(new SaveBarrier()).Options));
var commands = Service(factory);
CommandContext Context() => new(Guid.NewGuid(), "lpn-test");
await using var setup = factory.CreateDbContext();
try
{
    Check(!setup.Database.HasPendingModelChanges(), "Migration model has no drift");
    await setup.Database.MigrateAsync();
    foreach (var quantity in new[] { -1, 0, 501, int.MaxValue })
        Expect(await commands.IssueAsync(quantity, Context()), OperationErrorType.Invalid);
    Expect(await commands.IssueAsync(1, new(Guid.Empty, "test")), OperationErrorType.Invalid);
    Expect(await commands.IssueAsync(1, new(Guid.NewGuid(), "")), OperationErrorType.Invalid);
    Check(await setup.LicensePlateNumbers.CountAsync() == 0 && await setup.CommandReceipts.CountAsync() == 0,
        "Rejected inputs create no state or receipts");

    var context = Context();
    var batch = Value(await commands.IssueAsync(11, context));
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
    Check(Value(await commands.IssueAsync(11, context)) == batch, "Replay returns original batch across cultures");
    CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    Expect(await commands.IssueAsync(12, context), OperationErrorType.Conflict);
    var labels = await commands.GetForPrintAsync(batch, true);
    Check(labels.Count == 11, "One batch of requested size");
    Check(labels.Select(x => x.Code).SequenceEqual(Enumerable.Range(1, 11).Select(x => $"LPN{x:D12}")),
        "Database produces global sequential 12-digit codes");
    Check(labels.All(x => x.Batch.IssuedBy == "lpn-test" && x.Batch.IssuedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-2)),
        "Issuance author and time retained");
    var html = LicensePlateNumberPrintDocument.Render(labels);
    Directory.CreateDirectory("artifacts/lpn-check");
    await File.WriteAllTextAsync("artifacts/lpn-check/preview.html", html);
    Check(html == LicensePlateNumberPrintDocument.Render(await commands.GetForPrintAsync(batch, true)),
        "Reprint reads the same codes and identical document");
    Check(Regex.Matches(html, "<section class=\"sheet\">").Count == 2, "Eleven labels occupy two A4 sheets");
    Check((await commands.GetForPrintAsync(labels[0].Id, false)).Single().Code == labels[0].Code, "Single label reprint");
    Check((await commands.GetForPrintAsync(Guid.NewGuid(), true)).Count == 0, "Unknown batch is empty");
    Check((await commands.ListAsync(labels[0].Code, 0, 10)).Total == 1, "Search by exact code");
    Check((await commands.ListAsync(null, 10, 10)).Items.Count == 1, "Server pagination");
    DecodePrintedSvg(html, labels.Select(x => x.Code).ToArray());
    Console.WriteLine("PASS: migration, inputs, numbering, audit, replay/hash, search, pagination, A4 and SVG barcode decoding.");

    var before = await Counts();
    var racing = Racing(); var duplicate = Context();
    var same = await Task.WhenAll(racing.IssueAsync(20, duplicate), racing.IssueAsync(20, duplicate));
    Check(Value(same[0]) == Value(same[1]), "Concurrent duplicate commands return one batch");
    Check(await Counts() == (before.Lpns + 20, before.Batches + 1, before.Receipts + 1), "Losing duplicate batch rolls back");
    before = await Counts(); racing = Racing();
    var distinct = await Task.WhenAll(racing.IssueAsync(40, Context()), racing.IssueAsync(40, new(Guid.NewGuid(), "other-user")));
    Check(Value(distinct[0]) != Value(distinct[1]), "Independent concurrent batches both succeed");
    Check(await Counts() == (before.Lpns + 80, before.Batches + 2, before.Receipts + 2), "All independent labels retained");
    Check(await setup.LicensePlateNumbers.Select(x => x.Code).Distinct().CountAsync() == await setup.LicensePlateNumbers.CountAsync(),
        "Concurrent codes globally unique");
    before = await Counts(); racing = Racing(); var mismatch = Context();
    var mismatched = await Task.WhenAll(racing.IssueAsync(2, mismatch), racing.IssueAsync(3, mismatch));
    Check(mismatched.Count(x => x.IsSuccess) == 1, "Concurrent same id with different input has one winner");
    Expect(mismatched.Single(x => !x.IsSuccess), OperationErrorType.Conflict);
    Check((await Counts()).Batches == before.Batches + 1, "Mismatched losing batch rolls back");
    Console.WriteLine("PASS: SQL final-save races for independent batches, duplicate requests and different-input conflicts.");

    var lostFactory = new ContextFactory(new DbContextOptionsBuilder<ApplicationDbContext>(options)
        .AddInterceptors(new LoseResponse()).Options);
    var lost = Context(); before = await Counts();
    try { await Service(lostFactory).IssueAsync(4, lost); throw new Exception("Expected lost response"); }
    catch (IOException) { }
    var recovered = Value(await commands.IssueAsync(4, lost));
    Check((await commands.GetForPrintAsync(recovered, true)).Count == 4 &&
        await Counts() == (before.Lpns + 4, before.Batches + 1, before.Receipts + 1), "Lost committed response replays without another batch");

    before = await Counts(); var failed = Context();
    // Force the receipt insert to fail inside the final SQL transaction.
    await setup.Database.ExecuteSqlRawAsync("ALTER TABLE [CommandReceipts] WITH NOCHECK ADD CONSTRAINT [CK_LpnTest_ReceiptFailure] CHECK ([CommandType] <> 'lpn-label.issue-batch');");
    try { await commands.IssueAsync(6, failed); throw new Exception("Expected database failure"); }
    catch (DbUpdateException) { }
    finally { await setup.Database.ExecuteSqlRawAsync("ALTER TABLE [CommandReceipts] DROP CONSTRAINT [CK_LpnTest_ReceiptFailure];"); }
    Check(await Counts() == before, "Receipt failure leaves no orphan batch or labels");
    Value(await commands.IssueAsync(6, failed));
    Console.WriteLine("PASS: atomic rollback and retry after failure; replay after committed response loss.");

    // SQL uniqueness is a final guard even if a caller bypasses domain creation.
    try
    {
        await setup.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [LicensePlateNumbers] ([Id], [BatchId], [Code]) VALUES ({Guid.NewGuid()}, {batch}, {labels[0].Code});");
        throw new Exception("Expected duplicate code constraint");
    }
    catch (SqlException ex) when (ex.Number is 2601 or 2627) { }
    Check(await setup.InventoryBalances.CountAsync() == 0 && await setup.InventoryMovements.CountAsync() == 0 &&
        await setup.ReceivingOrders.CountAsync() == 0, "Issuance has no receiving or stock effects");
    var maximum = Value(await commands.IssueAsync(500, Context()));
    Check((await commands.GetForPrintAsync(maximum, true)).Count == 500, "Maximum allowed batch succeeds");
    Console.WriteLine("PASS: SQL unique constraint, maximum batch and absence of warehouse side effects.");

    if (args.Contains("--serve"))
    {
        // Browser verification uses this disposable database and a test-only confirmed account.
        var user = new ApplicationUser { Id = "lpn-ui-test", UserName = "lpn-test@example.invalid",
            NormalizedUserName = "LPN-TEST@EXAMPLE.INVALID", Email = "lpn-test@example.invalid",
            NormalizedEmail = "LPN-TEST@EXAMPLE.INVALID", EmailConfirmed = true,
            DisplayName = "Проверка I01", SecurityStamp = Guid.NewGuid().ToString() };
        user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, "Lpn-Test-2026!");
        setup.Users.Add(user); await setup.SaveChangesAsync();
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", connection);
        Environment.SetEnvironmentVariable("IdentityBootstrap__AdministratorEmail", "");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Console.WriteLine("Browser check: http://localhost:5189/lpn-labels (test-only account in README). Ctrl+C stops and removes the database.");
        await Wms.WebApp.Program.Main(["--urls", "http://localhost:5189", "--applicationName", "Wms.WebApp",
            "--contentRoot", Path.GetFullPath("Wms.WebApp"), "--Serilog:MinimumLevel:Default", "Warning",
            "--Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore.Database.Command", "Warning"]);
    }
    else
    {
        var lastIssued = await setup.LicensePlateNumbers.MaxAsync(x => x.Code);
        foreach (var script in new[] { "scripts/clear-wms-operational-data.sql", "scripts/clear-database-except-identity.sql" })
        {
            await setup.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(script));
            Check(await Counts() == (0, 0, 0), "Maintenance clears issuance and receipts");
            var afterCleanup = Value(await commands.IssueAsync(1, Context()));
            var nextCode = (await commands.GetForPrintAsync(afterCleanup, true)).Single().Code;
            Check(string.CompareOrdinal(nextCode, lastIssued) > 0, "Maintenance does not reuse printed numbers");
            lastIssued = nextCode;
        }
        Console.WriteLine("PASS: both maintenance scripts clear issuance without resetting the sequence.");
        await setup.Database.ExecuteSqlRawAsync("ALTER SEQUENCE [LicensePlateNumberSequence] RESTART WITH 999999999999;");
        var last = Value(await commands.IssueAsync(1, Context()));
        Check((await commands.GetForPrintAsync(last, true)).Single().Code == "LPN999999999999", "Last code keeps twelve digits");
        before = await Counts();
        try { await commands.IssueAsync(1, Context()); throw new Exception("Expected exhausted sequence"); }
        catch (DbUpdateException) { }
        Check(await Counts() == before, "Exhausted sequence never wraps or partially saves");
        Console.WriteLine("PASS: sequence upper boundary and no number reuse after exhaustion.");
    }

    async Task<(int Lpns, int Batches, int Receipts)> Counts()
    {
        await using var db = factory.CreateDbContext();
        return (await db.LicensePlateNumbers.CountAsync(), await db.LicensePlateNumberBatches.CountAsync(), await db.CommandReceipts.CountAsync());
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}
finally
{
    try { await setup.Database.EnsureDeletedAsync(); }
    catch (Exception exception) { Console.Error.WriteLine($"Test database cleanup failed: {exception.Message}"); Environment.ExitCode = 1; }
}

static void DecodePrintedSvg(string html, string[] codes)
{
    var svgs = Regex.Matches(html, "<svg[\\s\\S]*?</svg>");
    Check(svgs.Count == codes.Length, "One barcode per printed label");
    for (var i = 0; i < svgs.Count; i++)
    {
        var svg = XElement.Parse(svgs[i].Value);
        var viewBox = svg.Attribute("viewBox")!.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var width = int.Parse(viewBox[2], CultureInfo.InvariantCulture);
        var height = int.Parse(viewBox[3], CultureInfo.InvariantCulture);
        var pixels = Enumerable.Repeat((byte)255, width * height * 3).ToArray();
        foreach (var rect in svg.Descendants().Where(x => x.Name.LocalName == "rect" && x.Attribute("fill")?.Value != "#FFFFFF"))
        {
            int Attr(string name) => (int)double.Parse(rect.Attribute(name)!.Value, CultureInfo.InvariantCulture);
            var left = Attr("x"); var top = Attr("y");
            for (var y = top; y < top + Attr("height"); y++)
                for (var x = left; x < left + Attr("width"); x++)
                    Array.Fill(pixels, (byte)0, (y * width + x) * 3, 3);
        }
        var decoded = new BarcodeReaderGeneric().Decode(pixels, width, height, RGBLuminanceSource.BitmapFormat.RGB24);
        Check(decoded?.BarcodeFormat == BarcodeFormat.CODE_128 && decoded.Text == codes[i], "Printed SVG decodes exactly to its visible LPN");
    }
}
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static T Value<T>(OperationResult<T> result) { Check(result.IsSuccess, result.Error?.Message ?? "Expected success"); return result.Value!; }
static void Expect(OperationResult result, OperationErrorType type) => Check(result.Error?.Type == type, $"Expected {type}, got {result.Error}");
sealed class ContextFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
{ public ApplicationDbContext CreateDbContext() => new(options); }
sealed class SaveBarrier : SaveChangesInterceptor
{
    private int arrivals;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        return result;
    }
}
sealed class LoseResponse : SaveChangesInterceptor
{
    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
        throw new IOException("Simulated response loss after commit");
}
