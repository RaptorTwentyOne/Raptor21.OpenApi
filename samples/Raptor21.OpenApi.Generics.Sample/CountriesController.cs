using Microsoft.AspNetCore.Mvc;

using Raptor21.OpenApi.Generics.Sample.Contracts;

namespace Raptor21.OpenApi.Generics.Sample;

[ApiController]
[Route("[controller]")]
[Produces("application/json")]
public sealed class CountriesController : ControllerBase
{
    private static readonly CountryDto[] All =
    [
        new() { Code = "TR", Name = "Türkiye" },
        new() { Code = "DE", Name = "Deutschland" },
    ];

    /// <summary>
    /// Declares the payload, returns the envelope — the shape the automatic envelope exists to correct.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<CountryDto>), StatusCodes.Status200OK)]
    public IActionResult GetCountries()
        => Ok(BaseResponse<List<CountryDto>>.Success([.. All], StatusCodes.Status200OK));

    /// <summary>A single payload, to show the non-collection case.</summary>
    [HttpGet("{code}")]
    [ProducesResponseType(typeof(CountryDto), StatusCodes.Status200OK)]
    public IActionResult GetCountry(string code)
        => Ok(BaseResponse<CountryDto>.Success(All[0], StatusCodes.Status200OK));

    /// <summary>A container payload nested inside the envelope.</summary>
    [HttpGet("paged")]
    [ProducesResponseType(typeof(Page<CountryDto>), StatusCodes.Status200OK)]
    public IActionResult GetPaged()
        => Ok(BaseResponse<Page<CountryDto>>.Success(new Page<CountryDto> { Items = [.. All], TotalCount = All.Length }, StatusCodes.Status200OK));

    /// <summary>A binary download — not enveloped on the wire, so projection must leave it alone.</summary>
    [HttpGet("export")]
    [Produces("application/octet-stream")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public IActionResult Export() => File(new byte[] { 1, 2, 3 }, "application/octet-stream", "countries.bin");
}
