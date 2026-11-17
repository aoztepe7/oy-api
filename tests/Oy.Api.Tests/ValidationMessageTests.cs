using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Oy.Api.Infrastructure;

namespace Oy.Api.Tests;

public class ValidationMessageTests
{
    private sealed class Dto
    {
        [Required] public string? Name { get; set; }
        [MaxLength(5)] public string? Short { get; set; }
        [MinLength(3)] public string? Long { get; set; }
        [Range(1, 10)] public int Count { get; set; }
        [EmailAddress] public string? Email { get; set; }
        [Phone] public string? Phone { get; set; }
        [Url] public string? Site { get; set; }
    }

    private static Dictionary<string, string> Validate(Dto dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        return results.ToDictionary(r => r.MemberNames.Single(), r => ValidationMessages.Translate(r.ErrorMessage!));
    }

    [Fact]
    public void Translates_default_dataannotations_messages()
    {
        var r = Validate(new Dto { Name = null, Short = "abcdefg", Long = "a", Count = 99, Email = "x", Phone = "x", Site = "x" });
        Assert.Equal("Bu alan zorunludur.", r["Name"]);
        Assert.Equal("En fazla 5 karakter olabilir.", r["Short"]);
        Assert.Equal("En az 3 karakter olmalıdır.", r["Long"]);
        Assert.Equal("1 ile 10 arasında olmalıdır.", r["Count"]);
        Assert.Equal("Geçerli bir e-posta adresi girin.", r["Email"]);
        Assert.Equal("Geçerli bir telefon numarası girin.", r["Phone"]);
        Assert.Equal("Geçerli bir adres (URL) girin.", r["Site"]);
    }

    [Fact]
    public void Leaves_turkish_messages_unchanged() =>
        Assert.Equal("İşi yazın.", ValidationMessages.Translate("İşi yazın."));

    private static async Task<(HttpContext ctx, string body)> Handle(Exception ex)
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var ctx = new DefaultHttpContext { RequestServices = services };
        ctx.Response.Body = new MemoryStream();
        var handler = new BadRequestExceptionHandler(services.GetRequiredService<IProblemDetailsService>(), NullLogger<BadRequestExceptionHandler>.Instance);
        Assert.True(await handler.TryHandleAsync(ctx, ex, CancellationToken.None));
        ctx.Response.Body.Position = 0;
        return (ctx, await new StreamReader(ctx.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public async Task Json_bind_error_names_the_field_without_leaking_the_message()
    {
        var (ctx, body) = await Handle(new BadHttpRequestException("secret internal", 400, new JsonException("System.Foo leak", "$.dueDate", 0, 0)));
        Assert.Equal(400, ctx.Response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Geçersiz değer.", doc.RootElement.GetProperty("errors").GetProperty("dueDate")[0].GetString());
        Assert.DoesNotContain("leak", body);
        Assert.DoesNotContain("secret", body);
    }

    [Fact]
    public async Task Other_bad_requests_keep_their_status_with_a_turkish_title()
    {
        var (ctx, body) = await Handle(new BadHttpRequestException("x", 413));
        Assert.Equal(413, ctx.Response.StatusCode);
        Assert.Contains("Gönderilen istek okunamadı.", body);
    }

    [Fact]
    public async Task Unrelated_exceptions_are_not_handled()
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var handler = new BadRequestExceptionHandler(services.GetRequiredService<IProblemDetailsService>(), NullLogger<BadRequestExceptionHandler>.Instance);
        Assert.False(await handler.TryHandleAsync(new DefaultHttpContext(), new InvalidOperationException(), CancellationToken.None));
    }
}
