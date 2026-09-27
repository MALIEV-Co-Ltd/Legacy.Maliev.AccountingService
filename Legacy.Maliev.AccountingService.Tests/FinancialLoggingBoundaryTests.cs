using System.Reflection;
using Legacy.Maliev.AccountingService.Api.Controllers.Invoice;
using Maliev.Aspire.ServiceDefaults.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class FinancialLoggingBoundaryTests
{
    [Theory]
    [InlineData(typeof(InvoicesController), "invoices", "{invoice}", "/invoices/customer-secret")]
    [InlineData(typeof(Api.Controllers.Payment.PaymentsController), "payments", "{paymentId:int}", "/payments/81927")]
    [InlineData(typeof(Api.Controllers.Receipt.ReceiptsController), "receipts", "{receiptId:int}", "/receipts/81927")]
    public async Task UnhandledFinancialRequest_LogsOneCriticalRoutePatternWithoutLiteralData(
        Type controllerType,
        string controllerRoute,
        string actionRoute,
        string requestPath)
    {
        Assert.Equal(controllerRoute, controllerType.GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.Contains(controllerType.GetMethods(), method => method
            .GetCustomAttributes<HttpGetAttribute>()
            .Any(attribute => attribute.Template == actionRoute));

        var logger = new CapturingLogger<ExceptionHandlingMiddleware>();
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns("Production");
        environment.SetupGet(value => value.ApplicationName).Returns("Legacy.Maliev.AccountingService.Api");

        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = requestPath;
        context.Response.Body = new MemoryStream();
        var pattern = $"{controllerRoute}/{actionRoute}";
        context.SetEndpoint(new RouteEndpointBuilder(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(pattern),
            0).Build());

        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("protected-financial-detail"),
            logger,
            environment.Object);
        await middleware.InvokeAsync(context);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Critical, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Equal("UnhandledRequestFailure", entry.Values["EventName"]);
        Assert.Equal(pattern, entry.Values["Path"]);
        Assert.Equal("Legacy.Maliev.AccountingService.Api", entry.Values["Service"]);
        Assert.Equal(400, context.Response.StatusCode);
        Assert.DoesNotContain(requestPath, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("protected-financial-detail", entry.Message, StringComparison.Ordinal);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        Assert.DoesNotContain("protected-financial-detail", await reader.ReadToEndAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public void AccountingStartup_UsesLocalSharedLoggingAndNoRemovedNativeLoggingReference()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.AccountingService.Api", "Program.cs"));
        var project = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.AccountingService.Api", "Legacy.Maliev.AccountingService.Api.csproj"));
        var dockerfile = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.AccountingService.Api", "Dockerfile"));

        Assert.Contains("builder.AddServiceDefaults();", program, StringComparison.Ordinal);
        Assert.Contains("builder.AddStandardMiddleware", program, StringComparison.Ordinal);
        Assert.Contains("app.UseStandardMiddleware();", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Maliev.NativeLogging", program + project + dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("Maliev.InvoiceService.Api", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("Maliev.PaymentService.Api", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("Maliev.ReceiptService.Api", dockerfile, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.AccountingService.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("AccountingService solution root was not found.");
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> structured
                ? structured.ToDictionary(item => item.Key, item => item.Value)
                : new Dictionary<string, object?>();
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), values, exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message,
        IReadOnlyDictionary<string, object?> Values, Exception? Exception);

    private sealed class NoopScope : IDisposable
    {
        public static NoopScope Instance { get; } = new();

        public void Dispose() { }
    }
}
