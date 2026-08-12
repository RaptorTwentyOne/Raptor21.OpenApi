namespace Raptor21.OpenApi.Generics.Sample.Contracts;

/// <summary>
/// Stands in for a response envelope that lives in a shared package — the case where you cannot reach the
/// type to annotate it, so it is registered at startup instead.
/// </summary>
public class BaseResponse<T>
{
    public T? Data { get; set; }

    public int StatusCode { get; set; }

    public bool IsSuccessful => Errors.Count == 0;

    public List<string> Errors { get; set; } = [];

    public static BaseResponse<T> Success(T data, int statusCode) => new() { Data = data, StatusCode = statusCode };
}

/// <summary>A pagination container, to show container semantics nesting inside the envelope.</summary>
public class Page<TItem>
{
    public List<TItem> Items { get; set; } = [];

    public int PageNumber { get; set; }

    public int TotalCount { get; set; }
}

public sealed class CountryDto
{
    public required string Code { get; set; }

    public required string Name { get; set; }
}
